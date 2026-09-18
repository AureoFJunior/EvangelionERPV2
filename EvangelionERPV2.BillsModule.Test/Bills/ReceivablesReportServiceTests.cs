using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Linq.Expressions;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.BillsModule.Test
{
    public class ReceivablesReportServiceTests
    {
        private static readonly Guid EnterpriseId = Guid.NewGuid();

        [Theory]
        [InlineData(0, "notYetDue")]
        [InlineData(-5, "notYetDue")]
        [InlineData(1, "1to30")]
        [InlineData(30, "1to30")]
        [InlineData(31, "31to60")]
        [InlineData(60, "31to60")]
        [InlineData(61, "61to90")]
        [InlineData(90, "61to90")]
        [InlineData(91, "90plus")]
        public async Task GetAging_ShouldPlaceEachTitleInTheBucketItsAgeSays(int daysOverdue, string expectedBucket)
        {
            var today = DateTime.UtcNow.Date;
            var (order, bill) = BuildOrderWithBill(500, today.AddDays(-daysOverdue));

            var aging = await BuildSut([order], [bill]).GetAgingAsync(EnterpriseId);

            var amounts = new Dictionary<string, decimal>
            {
                ["notYetDue"] = aging.NotYetDue.Amount,
                ["1to30"] = aging.Days1To30.Amount,
                ["31to60"] = aging.Days31To60.Amount,
                ["61to90"] = aging.Days61To90.Amount,
                ["90plus"] = aging.Days90Plus.Amount
            };

            Assert.Equal(500, amounts[expectedBucket]);
            Assert.Equal(500, amounts.Values.Sum());
            Assert.Equal(500, aging.TotalAmount);
        }

        [Fact]
        public async Task GetAging_ShouldCountAnOrderWithNoBoleto()
        {
            // BillsSettings.Enabled is false in production: a carteira driven by Bill rows alone
            // would report nothing owed for a company that is owed plenty.
            var today = DateTime.UtcNow.Date;
            var order = BuildOrder(800);
            order.PaymentScheduledDate = today.AddDays(-45);

            var aging = await BuildSut([order], []).GetAgingAsync(EnterpriseId);

            Assert.Equal(800, aging.Days31To60.Amount);
            Assert.Equal(800, aging.TotalOverdueAmount);
        }

        [Fact]
        public async Task GetAging_ShouldDropOrdersSettledByTheirBoleto_EvenThoughOrderStatusNeverMoved()
        {
            // Settling a boleto does not touch Order.Status, so the title is the only record of it.
            var today = DateTime.UtcNow.Date;
            var (order, bill) = BuildOrderWithBill(300, today.AddDays(-10));
            bill.Status = BillStatus.Paid;
            bill.PaidAt = today.AddDays(-9);
            bill.PaidAmount = 300;

            var aging = await BuildSut([order], [bill]).GetAgingAsync(EnterpriseId);

            Assert.Equal(0, aging.TotalCount);
            Assert.Equal(0, aging.TotalAmount);
        }

        [Fact]
        public async Task GetAging_ShouldUseTheReissuedTitle_NotTheSupersededOne()
        {
            var today = DateTime.UtcNow.Date;
            var (order, cancelled) = BuildOrderWithBill(400, today.AddDays(-70));
            cancelled.Status = BillStatus.Cancelled;

            var reissued = BuildBill(order, 400, today.AddDays(-5));
            reissued.CreatedAt = cancelled.CreatedAt.AddDays(1);

            var aging = await BuildSut([order], [cancelled, reissued]).GetAgingAsync(EnterpriseId);

            Assert.Equal(400, aging.Days1To30.Amount);
            Assert.Equal(0, aging.Days61To90.Amount);
        }

        [Fact]
        public async Task GetAging_ShouldGroupByCustomer_HeaviestFirst()
        {
            var today = DateTime.UtcNow.Date;
            var small = BuildCustomer("Shinji");
            var large = BuildCustomer("Asuka");

            var smallOrder = BuildOrder(100, small);
            smallOrder.PaymentScheduledDate = today.AddDays(-2);
            var largeOrder = BuildOrder(2000, large);
            largeOrder.PaymentScheduledDate = today.AddDays(-2);

            var aging = await BuildSut([smallOrder, largeOrder], []).GetAgingAsync(EnterpriseId);

            Assert.Equal(2, aging.Customers.Count);
            Assert.Equal("Asuka", aging.Customers[0].CustomerName);
            Assert.Equal(2000, aging.Customers[0].Total);
        }

        [Fact]
        public async Task GetDelinquent_ShouldHonourTheThreshold_AndReportTheOldestDebt()
        {
            var today = DateTime.UtcNow.Date;
            var customer = BuildCustomer("Gendo");

            var recent = BuildOrder(100, customer);
            recent.PaymentScheduledDate = today.AddDays(-2);
            var old = BuildOrder(250, customer);
            old.PaymentScheduledDate = today.AddDays(-40);

            var service = BuildSut([recent, old], [], [customer]);

            var everything = await service.GetDelinquentAsync(EnterpriseId, 1);
            var row = Assert.Single(everything);
            Assert.Equal(2, row.OverdueCount);
            Assert.Equal(350, row.OverdueAmount);
            Assert.Equal(40, row.DaysOverdue);
            Assert.Equal("gendo@nerv.jp", row.Email);

            var onlyOld = await service.GetDelinquentAsync(EnterpriseId, 30);
            Assert.Equal(250, Assert.Single(onlyOld).OverdueAmount);
        }

        [Fact]
        public async Task GetDelinquent_ShouldIgnoreTitlesThatAreNotYetDue()
        {
            var today = DateTime.UtcNow.Date;
            var order = BuildOrder(900);
            order.PaymentScheduledDate = today.AddDays(5);

            Assert.Empty(await BuildSut([order], []).GetDelinquentAsync(EnterpriseId, 1));
        }

        [Fact]
        public async Task GetStatement_ShouldRunTheBalance_AndCarryTheOpeningForward()
        {
            var today = DateTime.UtcNow.Date;
            var customer = BuildCustomer("Misato");

            var before = BuildOrder(1000, customer);
            before.CreatedAt = today.AddDays(-120);

            var inside = BuildOrder(400, customer);
            inside.CreatedAt = today.AddDays(-10);
            var paidBill = BuildBill(inside, 400, today.AddDays(-5));
            paidBill.Status = BillStatus.Paid;
            paidBill.PaidAt = today.AddDays(-3);
            paidBill.PaidAmount = 400;

            var statement = await BuildSut([before, inside], [paidBill], [customer])
                .GetStatementAsync(EnterpriseId, customer.Id, today.AddDays(-30), today);

            Assert.NotNull(statement);
            Assert.Equal(1000, statement!.OpeningBalance);
            Assert.Equal(2, statement.Entries.Count);
            Assert.Equal(1400, statement.Entries[0].RunningBalance);
            Assert.Equal(1000, statement.Entries[1].RunningBalance);
            Assert.Equal(1000, statement.ClosingBalance);
        }

        [Fact]
        public async Task GetStatement_ShouldCreditARefundedOrder()
        {
            var today = DateTime.UtcNow.Date;
            var customer = BuildCustomer("Ritsuko");

            var order = BuildOrder(700, customer);
            order.CreatedAt = today.AddDays(-8);
            order.RefundedAt = today.AddDays(-4);
            order.RefundReason = "Wrong model";

            var statement = await BuildSut([order], [], [customer])
                .GetStatementAsync(EnterpriseId, customer.Id, today.AddDays(-30), today);

            Assert.NotNull(statement);
            Assert.Equal(0, statement!.ClosingBalance);
            Assert.Contains(statement.Entries, x => x.Kind == "refund" && x.Credit == 700);
        }

        [Fact]
        public async Task GetStatement_ShouldRefuseACustomerOfAnotherTenant()
        {
            var stranger = BuildCustomer("Kaworu");
            stranger.EnterpriseId = Guid.NewGuid();

            Assert.Null(await BuildSut([], [], [stranger])
                .GetStatementAsync(EnterpriseId, stranger.Id, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow));
        }

        private static Customer BuildCustomer(string name) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            IsActive = true,
            Name = name,
            Email = $"{name.ToLowerInvariant()}@nerv.jp",
            PhoneNumber = "+55 11 90000-0000"
        };

        private static Order BuildOrder(decimal total, Customer? customer = null)
        {
            customer ??= BuildCustomer("Rei");

            return new Order
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                IsActive = true,
                TotalValue = total,
                Status = (int)EnumOrderStatus.Pending,
                CustomerId = customer.Id,
                Customer = customer,
                CreatedAt = DateTime.UtcNow.AddDays(-15),
                PaymentScheduledDate = DateTime.UtcNow.Date.AddDays(30)
            };
        }

        private static Bill BuildBill(Order order, decimal amount, DateTime dueDate) => new()
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            IsActive = true,
            Amount = amount,
            DueDate = dueDate,
            DocumentNumber = "000001",
            Status = BillStatus.Registered,
            CreatedAt = DateTime.UtcNow.AddDays(-20)
        };

        /// <summary>
        /// The customer-scoped aging counts in SQL while the tenant-wide one builds a position per
        /// open order. Same bucket boundaries, same receivable rule — so for one customer they must
        /// produce the same five numbers.
        /// </summary>
        /// <remarks>
        /// The scoped overload exists because the tenant-wide one cost 1.1 GiB to read a single
        /// customer's row on a tenant with 1.8M open orders. That made the bucket rule exist twice.
        /// This is what keeps the two copies honest; if either side changes alone, it fails.
        /// </remarks>
        [Fact]
        public async Task TheScopedAgingAndTheTenantWideAging_AgreeForOneCustomer()
        {
            var today = DateTime.UtcNow.Date;
            var customer = BuildCustomer("Asuka");

            // One order per bucket, plus the boundary days themselves, plus two that must not count.
            var notYetDue = BuildOrderWithBillFor(customer, 100m, today.AddDays(10));
            var dueToday = BuildOrderWithBillFor(customer, 200m, today);
            var day1 = BuildOrderWithBillFor(customer, 300m, today.AddDays(-1));
            var day30 = BuildOrderWithBillFor(customer, 400m, today.AddDays(-30));
            var day31 = BuildOrderWithBillFor(customer, 500m, today.AddDays(-31));
            var day60 = BuildOrderWithBillFor(customer, 600m, today.AddDays(-60));
            var day61 = BuildOrderWithBillFor(customer, 700m, today.AddDays(-61));
            var day90 = BuildOrderWithBillFor(customer, 800m, today.AddDays(-90));
            var day91 = BuildOrderWithBillFor(customer, 900m, today.AddDays(-91));

            // Settled: off the carteira on both sides.
            var settled = BuildOrderWithBillFor(customer, 1000m, today.AddDays(-5));
            settled.bill.Status = BillStatus.Paid;

            // No title at all: dated by the order's own PaymentScheduledDate.
            var untitled = BuildOrder(1100m, customer);

            var withBills = new[] { notYetDue, dueToday, day1, day30, day31, day60, day61, day90, day91, settled };
            var orders = withBills.Select(x => x.order).Append(untitled).ToList();
            var bills = withBills.Select(x => x.bill).ToList();

            var sut = BuildSut(orders, bills, [customer]);

            var scoped = await sut.GetCustomerAgingAsync(EnterpriseId, customer.Id);
            var tenantWide = (await sut.GetAgingAsync(EnterpriseId)).Customers
                .Single(row => row.CustomerId == customer.Id);

            Assert.Equal(tenantWide.NotYetDue, scoped.NotYetDue);
            Assert.Equal(tenantWide.Days1To30, scoped.Days1To30);
            Assert.Equal(tenantWide.Days31To60, scoped.Days31To60);
            Assert.Equal(tenantWide.Days61To90, scoped.Days61To90);
            Assert.Equal(tenantWide.Days90Plus, scoped.Days90Plus);
            Assert.Equal(tenantWide.Total, scoped.Total);

            // Pinned, so a change that moves both sides the same way still fails. Due today counts
            // as not-yet-due (daysOverdue 0), and the untitled order is scheduled 30 days out.
            Assert.Equal(100m + 200m + 1100m, scoped.NotYetDue);
            Assert.Equal(300m + 400m, scoped.Days1To30);
            Assert.Equal(500m + 600m, scoped.Days31To60);
            Assert.Equal(700m + 800m, scoped.Days61To90);
            Assert.Equal(900m, scoped.Days90Plus);
        }

        [Fact]
        public async Task ScopedAging_IgnoresAnotherCustomersReceivables()
        {
            var today = DateTime.UtcNow.Date;
            var mine = BuildCustomer("Shinji");
            var theirs = BuildCustomer("Gendo");

            var ours = BuildOrderWithBillFor(mine, 250m, today.AddDays(-10));
            var other = BuildOrderWithBillFor(theirs, 9999m, today.AddDays(-10));

            var sut = BuildSut([ours.order, other.order], [ours.bill, other.bill], [mine, theirs]);

            var scoped = await sut.GetCustomerAgingAsync(EnterpriseId, mine.Id);

            Assert.Equal(250m, scoped.Days1To30);
            Assert.Equal(250m, scoped.Total);
        }

        private static (Order order, Bill bill) BuildOrderWithBillFor(Customer customer, decimal amount, DateTime dueDate)
        {
            var order = BuildOrder(amount, customer);
            return (order, BuildBill(order, amount, dueDate));
        }

        private static (Order order, Bill bill) BuildOrderWithBill(decimal amount, DateTime dueDate)
        {
            var order = BuildOrder(amount);
            return (order, BuildBill(order, amount, dueDate));
        }

        private static ReceivablesReportService BuildSut(
            List<Order> orders,
            List<Bill> bills,
            List<Customer>? customers = null)
        {
            return new ReceivablesReportService(
                Repo(orders).Object,
                Repo(bills).Object,
                // Like the real CustomerRepository: its GetAllAsync override throws on an empty
                // result, so a tenant with no customers must not take the carteira down with it.
                Repo(
                    customers ?? orders.Select(x => x.Customer!).Where(x => x != null).DistinctBy(x => x.Id).ToList(),
                    throwsWhenEmpty: true).Object,
                // Only GetCustomerAgingAsync reads through EF; the tenant-wide report still goes
                // through the repositories above, so this stays empty unless a test seeds it.
                BuildContext(orders, bills));
        }

        private static AppDbContext BuildContext(IEnumerable<Order> orders, IEnumerable<Bill> bills)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"receivables-report-{Guid.NewGuid():N}")
                .Options;

            var context = new AppDbContext(
                options,
                httpContextAccessor: null,
                auditTrailEntryFactory: new NoopAuditTrailEntryFactory());

            foreach (var order in orders)
                context.Set<Order>().Add(order);

            foreach (var bill in bills)
                context.Set<Bill>().Add(bill);

            context.SaveChanges();
            return context;
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

        private static Mock<IRepository<TEntity>> Repo<TEntity>(List<TEntity> records, bool throwsWhenEmpty = false)
            where TEntity : BaseEntity
        {
            var repo = new Mock<IRepository<TEntity>>();

            repo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<TEntity, bool>>?>()))
                .ReturnsAsync((Expression<Func<TEntity, bool>>? predicate) =>
                {
                    var result = predicate == null ? records.ToList() : records.Where(predicate.Compile()).ToList();
                    if (throwsWhenEmpty && result.Count == 0)
                        throw new NotFoundDatabaseException();

                    return result;
                });

            // The carteira reads orders through GetPagedAsync, which is the un-overridden path
            // with no Include graph behind it.
            repo.Setup(x => x.GetPagedAsync(
                    It.IsAny<Expression<Func<TEntity, bool>>?>(),
                    It.IsAny<Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync((
                    Expression<Func<TEntity, bool>>? predicate,
                    Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? _,
                    int? __,
                    int? ___) =>
                {
                    var items = predicate == null
                        ? records.ToList()
                        : records.Where(predicate.Compile()).ToList();

                    return ((IReadOnlyList<TEntity>)items, items.Count);
                });

            return repo;
        }
    }
}
