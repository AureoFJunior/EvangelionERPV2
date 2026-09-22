using EvangelionERPV2.PdvModule.Application.Services;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EvangelionERPV2.PdvModule.Test
{
    /// <summary>
    /// What a till sale looks like once it has become an <see cref="Order"/>: finished rather than
    /// merely paid, carrying the method that settled it, and named for a walk-in when nobody was
    /// identified.
    /// </summary>
    public class PdvSaleOrderShapeTests : IDisposable
    {
        private readonly PdvTestContext _database = new();
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();

        public void Dispose() => _database.Dispose();

        /// <summary>Captures the Order the service builds, without OrderService's ten collaborators.</summary>
        private static Mock<IOrderService<Order>> OrderServiceStub(AppDbContext context, List<Order> captured)
        {
            var mock = new Mock<IOrderService<Order>>();

            mock.Setup(service => service.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .Returns<Order, bool, bool, string?>(async (order, _, _, _) =>
                {
                    order.Id = Guid.NewGuid();
                    order.TotalValue = (order.OrderedProduct ?? [])
                        .Sum(line => decimal.Round(line.Value * line.Quantity, 2, MidpointRounding.AwayFromZero));

                    var lines = (order.OrderedProduct ?? []).ToList();
                    order.OrderedProduct = null;

                    context.Order.Add(order);
                    await context.SaveChangesAsync();

                    order.OrderedProduct = lines;
                    captured.Add(order);
                    return order;
                });

            return mock;
        }

        private static Mock<IProductService<Product>> ProductServiceStub()
        {
            var mock = new Mock<IProductService<Product>>();
            mock.Setup(service => service.GetThumbnailBase64Async(It.IsAny<string?>()))
                .ReturnsAsync((string.Empty, (string?)null));
            return mock;
        }

        private PdvService CreateService(AppDbContext context, Mock<IOrderService<Order>> orderService) =>
            new(context,
                new Repository<CashSession>(context),
                new Repository<CashMovement>(context),
                new Repository<PdvSale>(context),
                new Repository<PdvSalePayment>(context),
                orderService.Object,
                ProductServiceStub().Object,
                new Repository<PdvCharge>(context),
                PixProviderStubs.Object());

        private async Task<(PdvSaleSyncResultDTO Result, Order? Order)> SyncAsync(
            IEnumerable<PdvSalePaymentRequestDTO> payments, Guid? customerId = null)
        {
            var context = _database.CreateContext();
            var captured = new List<Order>();
            var service = CreateService(context, OrderServiceStub(context, captured));

            var sessionId = Guid.NewGuid();
            await service.OpenSessionAsync(new OpenCashSessionRequestDTO
            {
                ClientSessionId = sessionId,
                TerminalId = "CAIXA-01",
                OpeningFloat = 200m,
                OpenedAt = DateTime.UtcNow
            }, _enterpriseId, _userId);

            var tenders = payments.ToList();

            var result = await service.SyncSaleAsync(new PdvSaleSyncRequestDTO
            {
                ClientSaleId = Guid.NewGuid(),
                ClientSessionId = sessionId,
                TerminalId = "CAIXA-01",
                SoldAtLocal = DateTime.UtcNow,
                CustomerId = customerId,
                Items =
                [
                    new PdvSaleItemRequestDTO
                    {
                        ProductId = Guid.NewGuid(),
                        Quantity = 1m,
                        Value = tenders.Sum(payment => payment.Amount)
                    }
                ],
                Payments = tenders
            }, _enterpriseId, _userId);

            return (result, captured.SingleOrDefault());
        }

        private static PdvSalePaymentRequestDTO Tender(EnumPaymentMethod method, decimal amount) =>
            new()
            {
                Method = (int)method,
                Amount = amount,

                AuthorizationCode = method is EnumPaymentMethod.Credit or EnumPaymentMethod.Debit
                    ? "AUTH-1"
                    : null
            };

        [Fact]
        public async Task ASyncedSale_LandsOnTheOrderAsFinished()
        {
            var (result, order) = await SyncAsync([Tender(EnumPaymentMethod.Cash, 10m)]);

            Assert.Equal((int)EnumPdvSaleStatus.Synced, result.Status);
            Assert.NotNull(order);

            Assert.Equal((int)EnumOrderStatus.Finished, order.Status);
        }

        [Fact]
        public async Task ASaleSettledOneWay_RecordsThatMethodOnTheOrder()
        {
            var (_, order) = await SyncAsync([Tender(EnumPaymentMethod.Pix, 40m)]);

            Assert.Equal((int)EnumPaymentMethod.Pix, order!.PaymentMethod);
        }

        [Fact]
        public async Task TwoTendersOfTheSameMethod_AreStillThatMethod()
        {
            var (_, order) = await SyncAsync(
            [
                Tender(EnumPaymentMethod.Cash, 30m),
                Tender(EnumPaymentMethod.Cash, 20m)
            ]);

            Assert.Equal((int)EnumPaymentMethod.Cash, order!.PaymentMethod);
        }

        [Fact]
        public async Task ASaleSplitAcrossMethods_RecordsMixed()
        {
            var (_, order) = await SyncAsync(
            [
                Tender(EnumPaymentMethod.Cash, 30m),
                Tender(EnumPaymentMethod.Credit, 20m)
            ]);

            Assert.Equal((int)EnumPaymentMethod.Mixed, order!.PaymentMethod);
        }

        [Fact]
        public async Task ATenderSubmittedAsMixed_IsRejectedAndNoOrderIsCreated()
        {
            var (result, order) = await SyncAsync([Tender(EnumPaymentMethod.Mixed, 25m)]);

            Assert.Equal((int)EnumPdvSaleStatus.Rejected, result.Status);
            Assert.Null(result.OrderId);
            Assert.Null(order);
            Assert.Contains("Mixed", result.RejectReason);
        }

        [Fact]
        public async Task ASaleTiedToNoCustomer_ReadsAsVendaPdv()
        {
            var (_, order) = await SyncAsync([Tender(EnumPaymentMethod.Cash, 10m)]);

            Assert.Null(order!.CustomerId);

            var mapper = MapperConfig.RegisterMaps().CreateMapper();

            Assert.Equal("Venda PDV", mapper.Map<OrderDTO>(order).CustomerName);
        }

        [Fact]
        public void ASaleTiedToACustomer_KeepsTheCustomerName()
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                EnterpriseId = _enterpriseId,
                CustomerId = Guid.NewGuid(),
                Customer = new Customer { Name = "Padaria do Shinji" },
                Status = (int)EnumOrderStatus.Finished
            };

            var mapper = MapperConfig.RegisterMaps().CreateMapper();

            Assert.Equal("Padaria do Shinji", mapper.Map<OrderDTO>(order).CustomerName);
        }

        [Fact]
        public void AnOrderWhoseCustomerWasNotLoaded_IsNotAWalkIn()
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                EnterpriseId = _enterpriseId,
                CustomerId = Guid.NewGuid(),
                Customer = null,
                Status = (int)EnumOrderStatus.Paid
            };

            var mapper = MapperConfig.RegisterMaps().CreateMapper();

            Assert.Null(mapper.Map<OrderDTO>(order).CustomerName);
        }
    }
}
