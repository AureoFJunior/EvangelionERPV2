using System.Linq.Expressions;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.BillsModule.Test.Bills
{
    public class CustomerSummaryServiceTests
    {
        private static readonly Guid EnterpriseId = Guid.NewGuid();
        private static readonly Guid CustomerId = Guid.NewGuid();
        private static readonly Guid ProductId = Guid.NewGuid();

        private readonly Mock<IRepository<Customer>> _customers = new();
        private readonly Mock<IRepository<Order>> _orders = new();
        private readonly Mock<IRepository<Bill>> _bills = new();
        private readonly Mock<IRepository<OrderedProduct>> _lines = new();
        private readonly Mock<IRepository<Product>> _products = new();
        private readonly Mock<IReceivablesReportService> _receivables = new();

        private CustomerSummaryService BuildService() =>
            new(_customers.Object, _orders.Object, _bills.Object, _lines.Object, _products.Object, _receivables.Object);

        private void GivenCustomer(decimal? creditLimit = null)
        {
            _customers
                .Setup(r => r.GetPagedAsync(
                    It.IsAny<Expression<Func<Customer, bool>>?>(),
                    It.IsAny<Func<IQueryable<Customer>, IOrderedQueryable<Customer>>?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync(((IReadOnlyList<Customer>)new List<Customer>
                {
                    new() { Id = CustomerId, EnterpriseId = EnterpriseId, IsActive = true, Name = "Nerv Ltda", Email = "contato@nerv.jp", CreditLimit = creditLimit }
                }, 1));
        }

        private void GivenOrders(IEnumerable<Order> orders, IEnumerable<Bill> bills)
        {
            _orders.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>?>())).ReturnsAsync(orders);
            _bills.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Bill, bool>>?>())).ReturnsAsync(bills);
            _lines.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<OrderedProduct, bool>>?>())).ReturnsAsync(Array.Empty<OrderedProduct>());
            _products.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Product, bool>>?>())).ReturnsAsync(Array.Empty<Product>());
            _receivables.Setup(r => r.GetAgingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReceivablesAgingDTO());
        }

        private static Order Order(decimal amount, DateTime createdAt, bool refunded = false) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            CustomerId = CustomerId,
            TotalValue = amount,
            IsActive = true,
            CreatedAt = createdAt,
            Status = refunded ? (int)EnumOrderStatus.Refund : (int)EnumOrderStatus.Pending,
            RefundedAt = refunded ? createdAt : null
        };

        [Fact]
        public async Task AnUnknownCustomer_IsNull_NotAnEmptyFicha()
        {
            _customers
                .Setup(r => r.GetPagedAsync(
                    It.IsAny<Expression<Func<Customer, bool>>?>(),
                    It.IsAny<Func<IQueryable<Customer>, IOrderedQueryable<Customer>>?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync(((IReadOnlyList<Customer>)new List<Customer>(), 0));

            Assert.Null(await BuildService().GetSummaryAsync(CustomerId, EnterpriseId, includeFinancial: true));
        }

        [Fact]
        public async Task History_ExcludesRefundsFromWhatTheCustomerBought()
        {
            GivenCustomer();
            GivenOrders(
                new[]
                {
                    Order(1000m, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
                    Order(500m, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)),
                    Order(9000m, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), refunded: true)
                },
                Array.Empty<Bill>());

            var summary = await BuildService().GetSummaryAsync(CustomerId, EnterpriseId, includeFinancial: false);

            Assert.NotNull(summary);
            Assert.Equal(2, summary!.OrderCount);
            Assert.Equal(1500m, summary.TotalPurchased);
            Assert.Equal(750m, summary.AverageTicket);
            Assert.Equal(new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), summary.FirstPurchaseAtUtc);
            // The refunded order still shows in the history — it happened — but counts for nothing.
            Assert.Equal(3, summary.RecentOrders.Count());
        }

        [Fact]
        public async Task WithoutBillsRead_TheMoneyBlockIsAbsentButTheFichaStillRenders()
        {
            GivenCustomer(creditLimit: 5000m);
            GivenOrders(new[] { Order(1000m, DateTime.UtcNow) }, Array.Empty<Bill>());

            var summary = await BuildService().GetSummaryAsync(CustomerId, EnterpriseId, includeFinancial: false);

            Assert.NotNull(summary);
            Assert.Null(summary!.Financial);
            Assert.Equal("Nerv Ltda", summary.CustomerName);
        }

        [Fact]
        public async Task Exposure_DropsToZeroOnceTheBoletoIsSettled()
        {
            var settled = Order(1000m, DateTime.UtcNow);
            var open = Order(400m, DateTime.UtcNow);

            GivenCustomer(creditLimit: 5000m);
            GivenOrders(
                new[] { settled, open },
                new[]
                {
                    new Bill { Id = Guid.NewGuid(), OrderId = settled.Id, Amount = 1000m, Status = BillStatus.Paid, PaidAmount = 1000m, IsActive = true },
                    new Bill { Id = Guid.NewGuid(), OrderId = open.Id, Amount = 400m, Status = BillStatus.Registered, IsActive = true }
                });

            var financial = (await BuildService().GetSummaryAsync(CustomerId, EnterpriseId, includeFinancial: true))!.Financial;

            Assert.NotNull(financial);
            Assert.Equal(400m, financial!.OpenExposure);
            Assert.Equal(4600m, financial.CreditAvailable);
        }

        [Fact]
        public async Task WithoutACreditLimit_AvailableIsNull_NotZero()
        {
            GivenCustomer(creditLimit: null);
            GivenOrders(new[] { Order(100m, DateTime.UtcNow) }, Array.Empty<Bill>());

            var financial = (await BuildService().GetSummaryAsync(CustomerId, EnterpriseId, includeFinancial: true))!.Financial;

            Assert.Null(financial!.CreditLimit);
            Assert.Null(financial.CreditAvailable);
        }

        [Fact]
        public async Task TopProducts_RankByRevenue()
        {
            var order = Order(1000m, DateTime.UtcNow);
            var otherProductId = Guid.NewGuid();

            GivenCustomer();
            _orders.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>?>())).ReturnsAsync(new[] { order });
            _bills.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Bill, bool>>?>())).ReturnsAsync(Array.Empty<Bill>());
            _lines.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<OrderedProduct, bool>>?>())).ReturnsAsync(new[]
            {
                new OrderedProduct { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = ProductId, Quantity = 2m, Value = 100m, IsActive = true },
                new OrderedProduct { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = otherProductId, Quantity = 1m, Value = 800m, IsActive = true }
            });
            _products.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Product, bool>>?>())).ReturnsAsync(new[]
            {
                new Product { Id = ProductId, EnterpriseId = EnterpriseId, Name = "LCL" },
                new Product { Id = otherProductId, EnterpriseId = EnterpriseId, Name = "Entry Plug" }
            });

            var top = (await BuildService().GetSummaryAsync(CustomerId, EnterpriseId, includeFinancial: false))!.TopProducts.ToList();

            Assert.Equal("Entry Plug", top[0].ProductName);
            Assert.Equal(800m, top[0].Revenue);
            Assert.Equal(200m, top[1].Revenue);
        }
    }
}
