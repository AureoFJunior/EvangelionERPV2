using System.Linq.Expressions;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Moq;

namespace EvangelionERPV2.BillsModule.Test.Bills
{
    public class CustomerSummaryServiceTests : IDisposable
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

        private readonly AppDbContext _context = BuildContext();

        private CustomerSummaryService BuildService() =>
            new(_customers.Object, _orders.Object, _bills.Object, _lines.Object, _products.Object, _receivables.Object, _context);

        private static AppDbContext BuildContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"customer-summary-{Guid.NewGuid():N}")
                .Options;

            return new AppDbContext(
                options,
                httpContextAccessor: null,
                auditTrailEntryFactory: new NoopAuditTrailEntryFactory());
        }

        private void Seed(IEnumerable<Order>? orders = null, IEnumerable<Bill>? bills = null, IEnumerable<OrderedProduct>? lines = null)
        {
            foreach (var order in orders ?? []) _context.Set<Order>().Add(order);
            foreach (var bill in bills ?? []) _context.Set<Bill>().Add(bill);
            foreach (var line in lines ?? []) _context.Set<OrderedProduct>().Add(line);
            _context.SaveChanges();
        }

        public void Dispose()
        {
            _context.Dispose();
            GC.SuppressFinalize(this);
        }

        private sealed class NoopAuditTrailEntryFactory : IAuditTrailEntryFactory
        {
            public IReadOnlyCollection<AuditTrail> Create(
                IEnumerable<EntityEntry<BaseEntity>> entries,
                Guid? userId,
                Guid? enterpriseId,
                DateTime changedAt)
            {
                return Array.Empty<AuditTrail>();
            }
        }

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
            Seed(orders, bills);
            _products.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Product, bool>>?>())).ReturnsAsync(Array.Empty<Product>());
            _receivables.Setup(r => r.GetCustomerAgingAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReceivablesAgingCustomerDTO());
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

        /// <summary>
        /// Open exposure is now defined twice — once as a SQL expression in CustomerSummaryService
        /// and once as <see cref="ReceivableResolver.ResolveOpenExposure"/>. This runs the same
        /// cases through both and asserts they agree.
        /// </summary>
        [Fact]
        public async Task TheQueryAndTheResolver_AgreeOnOpenExposure()
        {
            var noBill = Order(1000m, DateTime.UtcNow);
            var settled = Order(500m, DateTime.UtcNow);
            var settledShort = Order(500m, DateTime.UtcNow);
            var open = Order(400m, DateTime.UtcNow);
            var openZeroAmount = Order(250m, DateTime.UtcNow);
            var rejected = Order(300m, DateTime.UtcNow);
            var cancelledOnly = Order(600m, DateTime.UtcNow);
            var refunded = Order(9000m, DateTime.UtcNow, refunded: true);

            var bills = new[]
            {
                new Bill { Id = Guid.NewGuid(), OrderId = settled.Id, Amount = 500m, PaidAmount = 500m, Status = BillStatus.Paid, IsActive = true },
                new Bill { Id = Guid.NewGuid(), OrderId = settledShort.Id, Amount = 500m, PaidAmount = 450m, Status = BillStatus.Paid, IsActive = true },
                new Bill { Id = Guid.NewGuid(), OrderId = open.Id, Amount = 400m, Status = BillStatus.Registered, IsActive = true },
                new Bill { Id = Guid.NewGuid(), OrderId = openZeroAmount.Id, Amount = 0m, Status = BillStatus.Registered, IsActive = true },
                new Bill { Id = Guid.NewGuid(), OrderId = rejected.Id, Amount = 300m, Status = BillStatus.Rejected, IsActive = true },
                new Bill { Id = Guid.NewGuid(), OrderId = cancelledOnly.Id, Amount = 600m, Status = BillStatus.Cancelled, IsActive = true },
                new Bill { Id = Guid.NewGuid(), OrderId = refunded.Id, Amount = 9000m, Status = BillStatus.Registered, IsActive = true }
            };

            var orders = new[] { noBill, settled, settledShort, open, openZeroAmount, rejected, cancelledOnly, refunded };

            GivenCustomer(creditLimit: 100_000m);
            GivenOrders(orders, bills);

            var fromQuery = (await BuildService().GetSummaryAsync(CustomerId, EnterpriseId, includeFinancial: true))!
                .Financial!.OpenExposure;

            var liveBills = ReceivableResolver.SelectLiveBillsByOrder(bills);
            var fromResolver = Math.Round(
                orders
                    .Where(order => !ReceivableResolver.IsOrderRefunded(order))
                    .Sum(order => ReceivableResolver.ResolveOpenExposure(
                        order,
                        liveBills.TryGetValue(order.Id, out var bill) ? bill : null)),
                2,
                MidpointRounding.AwayFromZero);

            Assert.Equal(fromResolver, fromQuery);

            Assert.Equal(2550m, fromQuery);
        }

        [Fact]
        public async Task TopProducts_RankByRevenue()
        {
            var order = Order(1000m, DateTime.UtcNow);
            var otherProductId = Guid.NewGuid();

            GivenCustomer();
            Seed(
                orders: new[] { order },
                lines: new[]
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
