using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using System.Linq.Expressions;
using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.OrderModule.Test.Orders
{
    /// <summary>
    /// The rule worth pinning: commission follows what was <em>received</em>, not what was sold.
    /// Paying on an unsettled invoice is how a company funds its own bad debt.
    /// </summary>
    public class SalesPerformanceServiceTests
    {
        private static readonly Guid EnterpriseId = Guid.NewGuid();
        private static readonly Guid SellerId = Guid.NewGuid();

        private readonly Mock<IRepository<Order>> _orders = new();
        private readonly Mock<IRepository<Bill>> _bills = new();
        private readonly Mock<IRepository<User>> _users = new();
        private readonly Mock<IRepository<SalesGoal>> _goals = new();

        private AppDbContext _context = null!;

        private readonly RecordingCache _cache = new();

        private static IConfiguration CacheConfig(bool enabled) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["SalesPerformance:CacheEnabled"] = enabled ? "true" : "false" })
                .Build();

        private SalesPerformanceService BuildService() =>
            new(_context, _cache, CacheConfig(true), _users.Object, _goals.Object);

        private void Given(IEnumerable<Order> orders, IEnumerable<Bill> bills, decimal? commissionRate, decimal? target)
        {
            SeedContext(orders, bills, new User
            {
                Id = SellerId,
                EnterpriseId = EnterpriseId,
                IsActive = true,
                FirstName = "Rei",
                LastName = "Ayanami",
                CommissionRate = commissionRate
            });

            _goals.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<SalesGoal, bool>>?>())).ReturnsAsync(
                target.HasValue
                    ? new[] { new SalesGoal { Id = Guid.NewGuid(), EnterpriseId = EnterpriseId, UserId = SellerId, Year = 2026, Month = 8, TargetAmount = target.Value, IsActive = true } }
                    : Array.Empty<SalesGoal>());
        }

        /// <summary>
        /// Seeds the in-memory context the aggregate reads from. Tests that need more than the one
        /// default seller - a service account, say - call this directly.
        /// </summary>
        private void SeedContext(IEnumerable<Order> orders, IEnumerable<Bill> bills, params User[] users)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"sales-performance-tests-{Guid.NewGuid():N}")
                .Options;

            _context = new AppDbContext(options, httpContextAccessor: null, auditTrailEntryFactory: new NoopAuditTrailEntryFactory());
            _context.Database.EnsureCreated();

            foreach (var user in users)
                _context.Set<User>().Add(user);

            foreach (var order in orders)
            {
                order.EnterpriseId ??= EnterpriseId;
                order.IsActive ??= true;
                _context.Set<Order>().Add(order);
            }

            foreach (var bill in bills)
            {
                bill.IsActive ??= true;
                _context.Set<Bill>().Add(bill);
            }

            _context.SaveChanges();
        }

        private sealed class RecordingCache : IDistributedCache
        {
            public List<string> WrittenKeys { get; } = [];
            public List<string> WrittenValues { get; } = [];

            public byte[]? Get(string key) => null;
            public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
            public void Refresh(string key) { }
            public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
            public void Remove(string key) { }
            public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
            public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            {
                WrittenKeys.Add(key);
                WrittenValues.Add(System.Text.Encoding.UTF8.GetString(value));
            }

            public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            {
                WrittenKeys.Add(key);
                WrittenValues.Add(System.Text.Encoding.UTF8.GetString(value));
                return Task.CompletedTask;
            }
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

        [Fact]
        public async Task GetSellerPerformance_AnswersTheSellersOwnRowAndNothingElse()
        {
            Given([Sale(1000m, payday: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc))], [], commissionRate: 0.05m, target: 800m);

            var row = await BuildService().GetSellerPerformanceAsync(EnterpriseId, SellerId, 2026, 8);

            Assert.NotNull(row);
            Assert.Equal(SellerId, row!.UserId);
            Assert.Equal(1000m, row.SoldAmount);
        }

        [Fact]
        public async Task GetSellerPerformance_OfSomeoneWhoSoldNothing_AnswersNothing()
        {
            Given([], [], commissionRate: 0.05m, target: null);

            Assert.Null(await BuildService().GetSellerPerformanceAsync(EnterpriseId, SellerId, 2026, 8));
            Assert.Null(await BuildService().GetSellerPerformanceAsync(EnterpriseId, Guid.Empty, 2026, 8));
        }

        [Fact]
        public async Task GetSellerPerformance_AgreesWithTheManagersTable()
        {
            Given([Sale(1000m, payday: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc))], [], commissionRate: 0.05m, target: 800m);

            var service = BuildService();
            var table = await service.GetPerformanceAsync(EnterpriseId, 2026, 8);
            var row = await service.GetSellerPerformanceAsync(EnterpriseId, SellerId, 2026, 8);

            var managerRow = table.Sellers.Single(x => x.UserId == SellerId);
            Assert.Equal(managerRow.SoldAmount, row!.SoldAmount);
            Assert.Equal(managerRow.ReceivedAmount, row.ReceivedAmount);
            Assert.Equal(managerRow.CommissionAmount, row.CommissionAmount);
        }

        private static Order Sale(decimal amount, DateTime? payday = null, bool refunded = false) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            UserId = SellerId,
            TotalValue = amount,
            Payday = payday,
            IsActive = true,
            CreatedAt = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc),
            Status = refunded ? (int)EnumOrderStatus.Refund : (int)EnumOrderStatus.Pending,
            RefundedAt = refunded ? new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc) : null
        };

        [Fact]
        public async Task Commission_IsPaidOnWhatWasReceived_NotOnWhatWasSold()
        {
            var settled = Sale(1000m);
            var unsettled = Sale(500m);

            Given(
                new[] { settled, unsettled },
                new[]
                {
                    new Bill { Id = Guid.NewGuid(), OrderId = settled.Id, Amount = 1000m, Status = BillStatus.Paid, PaidAmount = 1000m, PaidAt = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc), IsActive = true },
                    new Bill { Id = Guid.NewGuid(), OrderId = unsettled.Id, Amount = 500m, Status = BillStatus.Registered, IsActive = true }
                },
                commissionRate: 0.05m,
                target: null);

            var row = (await BuildService().GetPerformanceAsync(EnterpriseId, 2026, 8)).Sellers.Single();

            Assert.Equal(1500m, row.SoldAmount);
            Assert.Equal(1000m, row.ReceivedAmount);
            Assert.Equal(50m, row.CommissionAmount);
        }

        [Fact]
        public async Task AnOrderWithoutABoleto_CountsAsReceivedOnceItHasAPayday()
        {
            Given(
                new[] { Sale(300m, payday: new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc)), Sale(200m) },
                Array.Empty<Bill>(),
                commissionRate: 0.10m,
                target: null);

            var row = (await BuildService().GetPerformanceAsync(EnterpriseId, 2026, 8)).Sellers.Single();

            Assert.Equal(500m, row.SoldAmount);
            Assert.Equal(300m, row.ReceivedAmount);
            Assert.Equal(30m, row.CommissionAmount);
        }

        [Fact]
        public async Task ARefundedOrder_CountsForNothing()
        {
            Given(
                new[] { Sale(1000m, payday: new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc), refunded: true), Sale(100m) },
                Array.Empty<Bill>(),
                commissionRate: 0.05m,
                target: null);

            var row = (await BuildService().GetPerformanceAsync(EnterpriseId, 2026, 8)).Sellers.Single();

            Assert.Equal(100m, row.SoldAmount);
            Assert.Equal(0m, row.ReceivedAmount);
            Assert.Equal(1, row.OrderCount);
        }

        [Fact]
        public async Task WithoutARate_ThereIsNoCommissionColumn_NotAZero()
        {
            Given(new[] { Sale(400m, payday: DateTime.UtcNow) }, Array.Empty<Bill>(), commissionRate: null, target: null);

            var row = (await BuildService().GetPerformanceAsync(EnterpriseId, 2026, 8)).Sellers.Single();

            Assert.Null(row.CommissionRate);
            Assert.Null(row.CommissionAmount);
        }

        [Fact]
        public async Task Attainment_IsNullWithoutATarget_AndAPercentageWithOne()
        {
            Given(new[] { Sale(750m) }, Array.Empty<Bill>(), commissionRate: null, target: null);
            Assert.Null((await BuildService().GetPerformanceAsync(EnterpriseId, 2026, 8)).Sellers.Single().AttainmentPercent);

            Given(new[] { Sale(750m) }, Array.Empty<Bill>(), commissionRate: null, target: 1000m);
            var row = (await BuildService().GetPerformanceAsync(EnterpriseId, 2026, 8)).Sellers.Single();

            Assert.Equal(1000m, row.TargetAmount);
            Assert.Equal(75m, row.AttainmentPercent);
        }

        [Fact]
        public async Task AMonthWithNoOrders_IsAnEmptyReport_NotAFailure()
        {
            Given(Array.Empty<Order>(), Array.Empty<Bill>(), commissionRate: 0.05m, target: 1000m);

            var performance = await BuildService().GetPerformanceAsync(EnterpriseId, 2026, 8);

            Assert.Empty(performance.Sellers);
            Assert.Equal(0m, performance.Enterprise.SoldAmount);
        }

        [Theory]
        [InlineData(1999, 8)]
        [InlineData(2026, 0)]
        [InlineData(2026, 13)]
        public async Task AnImpossibleCompetencia_IsRefused(int year, int month)
        {
            Given(Array.Empty<Order>(), Array.Empty<Bill>(), null, null);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => BuildService().GetPerformanceAsync(EnterpriseId, year, month));
        }

        [Fact]
        public async Task UpsertGoal_ReplacesTheTargetForTheSameCompetencia()
        {
            var existing = new SalesGoal
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                UserId = SellerId,
                Year = 2026,
                Month = 8,
                TargetAmount = 1000m,
                IsActive = true
            };

            _users.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<User, bool>>?>())).ReturnsAsync(new[]
            {
                new User { Id = SellerId, EnterpriseId = EnterpriseId, IsActive = true }
            });
            _goals.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<SalesGoal, bool>>?>())).ReturnsAsync(new[] { existing });
            _goals.Setup(r => r.Update(It.IsAny<SalesGoal>())).Returns((SalesGoal goal) => goal);

            var updated = await BuildService().UpsertGoalAsync(EnterpriseId, new UpsertSalesGoalRequestDTO
            {
                UserId = SellerId,
                Year = 2026,
                Month = 8,
                TargetAmount = 2500m
            });

            Assert.Equal(existing.Id, updated.Id);
            Assert.Equal(2500m, updated.TargetAmount);
            _goals.Verify(r => r.CreateAsync(It.IsAny<SalesGoal>()), Times.Never);
        }

        [Fact]
        public async Task UpsertGoal_RefusesASellerFromAnotherEnterprise()
        {
            _users.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<User, bool>>?>())).ReturnsAsync(Array.Empty<User>());

            await Assert.ThrowsAsync<NotFoundDatabaseException>(() => BuildService().UpsertGoalAsync(
                EnterpriseId,
                new UpsertSalesGoalRequestDTO { UserId = Guid.NewGuid(), Year = 2026, Month = 8, TargetAmount = 100m }));
        }

        /// <summary>
        /// Like the setups above, but runs the predicate the service built instead of ignoring it.
        /// The service-account rule lives in that expression, so a mock that answers every query
        /// with the same list cannot tell whether it works.
        /// </summary>
        private void GivenFilteredUsers(params User[] users)
        {
            _users.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<User, bool>>?>()))
                .ReturnsAsync((Expression<Func<User, bool>>? predicate) =>
                    users.Where(predicate?.Compile() ?? (_ => true)).ToList());
        }

        private static User Person(Guid id) => new()
        {
            Id = id,
            EnterpriseId = EnterpriseId,
            IsActive = true,
            FirstName = "Rei",
            LastName = "Ayanami"
        };

        private static User Robot(Guid id) => new()
        {
            Id = id,
            EnterpriseId = EnterpriseId,
            IsActive = true,
            FirstName = "Order",
            LastName = "Worker",
            IsServiceAccount = true
        };

        [Fact]
        public async Task AServiceAccount_GetsNoSellerRow_ButItsSalesStillCountForTheCompany()
        {
            var robotId = Guid.NewGuid();
            var robotSale = Sale(400m);
            robotSale.UserId = robotId;

            _goals.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<SalesGoal, bool>>?>())).ReturnsAsync([]);
            SeedContext([Sale(1000m), robotSale], [], Person(SellerId), Robot(robotId));
            GivenFilteredUsers(Person(SellerId), Robot(robotId));

            var performance = await BuildService().GetPerformanceAsync(EnterpriseId, 2026, 8);

            Assert.Equal([SellerId], performance.Sellers.Select(row => row.UserId).ToArray());
            Assert.Equal(1400m, performance.Enterprise.SoldAmount);
            Assert.Equal(2, performance.Enterprise.OrderCount);
        }

        [Fact]
        public async Task AServiceAccount_CannotBeGivenATarget()
        {
            var robotId = Guid.NewGuid();
            GivenFilteredUsers(Robot(robotId));

            await Assert.ThrowsAsync<NotFoundDatabaseException>(() => BuildService().UpsertGoalAsync(
                EnterpriseId,
                new UpsertSalesGoalRequestDTO { UserId = robotId, Year = 2026, Month = 8, TargetAmount = 100m }));
        }

        [Fact]
        public async Task AServiceAccountsOldTarget_IsNotListed()
        {
            var robotId = Guid.NewGuid();
            GivenFilteredUsers(Person(SellerId), Robot(robotId));
            _goals.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<SalesGoal, bool>>?>())).ReturnsAsync(
            [
                new SalesGoal { Id = Guid.NewGuid(), EnterpriseId = EnterpriseId, UserId = SellerId, Year = 2026, Month = 8, TargetAmount = 500m, IsActive = true },
                new SalesGoal { Id = Guid.NewGuid(), EnterpriseId = EnterpriseId, UserId = robotId, Year = 2026, Month = 8, TargetAmount = 900m, IsActive = true },
                new SalesGoal { Id = Guid.NewGuid(), EnterpriseId = EnterpriseId, UserId = null, Year = 2026, Month = 8, TargetAmount = 5000m, IsActive = true }
            ]);

            var goals = await BuildService().GetGoalsAsync(EnterpriseId, 2026);

            Assert.Equal(2, goals.Count);
            Assert.DoesNotContain(goals, goal => goal.UserId == robotId);
        }

        /// <summary>
        /// The cache key has to carry the tenant. Keyed on year and month alone, the first
        /// enterprise to ask for a month would have its sellers, their names and their commissions
        /// served to every other enterprise asking for the same month.
        /// </summary>
        [Fact]
        public async Task CacheKey_IsScopedToTheTenant()
        {
            Given([Sale(1000m)], [], commissionRate: 0.05m, target: null);

            var otherEnterprise = Guid.NewGuid();
            var service = BuildService();

            await service.GetPerformanceAsync(EnterpriseId, 2026, 8);
            await service.GetPerformanceAsync(otherEnterprise, 2026, 8);

            Assert.Equal(2, _cache.WrittenKeys.Count);
            Assert.Contains(_cache.WrittenKeys, key => key.Contains(EnterpriseId.ToString()));
            Assert.Contains(_cache.WrittenKeys, key => key.Contains(otherEnterprise.ToString()));
            Assert.Equal(2, _cache.WrittenKeys.Distinct().Count());
        }

        [Fact]
        public async Task CacheKey_SeparatesCompetencias()
        {
            Given([Sale(1000m)], [], commissionRate: null, target: null);

            var service = BuildService();
            await service.GetPerformanceAsync(EnterpriseId, 2026, 8);
            await service.GetPerformanceAsync(EnterpriseId, 2026, 9);

            Assert.Equal(2, _cache.WrittenKeys.Distinct().Count());
        }

        /// <summary>
        /// A cache that cannot be reached must not take the report down with it - the numbers are
        /// still computable, they are just not free.
        /// </summary>
        [Fact]
        public async Task ABrokenCache_DoesNotFailTheReport()
        {
            Given([Sale(1000m, payday: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc))], [], commissionRate: 0.05m, target: null);

            var service = new SalesPerformanceService(_context, new ThrowingCache(), CacheConfig(true), _users.Object, _goals.Object);

            var performance = await service.GetPerformanceAsync(EnterpriseId, 2026, 8);

            Assert.Equal(1000m, performance.Enterprise.SoldAmount);
        }

        private sealed class ThrowingCache : IDistributedCache
        {
            public byte[]? Get(string key) => throw new InvalidOperationException("cache down");
            public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("cache down");
            public void Refresh(string key) => throw new InvalidOperationException("cache down");
            public Task RefreshAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("cache down");
            public void Remove(string key) => throw new InvalidOperationException("cache down");
            public Task RemoveAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("cache down");
            public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new InvalidOperationException("cache down");
            public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new InvalidOperationException("cache down");
        }

        /// <summary>
        /// Redis is a third-party service here, so a seller's name must not be written to it. The
        /// numbers may go - keyed by user id they are pseudonymous - but the name is personal data
        /// and stays in the process.
        /// </summary>
        [Fact]
        public async Task CachedPayload_CarriesNoSellerNames()
        {
            Given([Sale(1000m, payday: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc))], [], commissionRate: 0.05m, target: null);

            var performance = await BuildService().GetPerformanceAsync(EnterpriseId, 2026, 8);

            Assert.Equal("Rei Ayanami", performance.Sellers.Single().SellerName);

            var written = Assert.Single(_cache.WrittenValues);
            Assert.DoesNotContain("Rei", written);
            Assert.DoesNotContain("Ayanami", written);
            Assert.Contains(SellerId.ToString(), written);
            Assert.Contains("1000", written);
        }

        /// <summary>
        /// The drift guard.
        /// <para>
        /// "What has been received" is now defined twice: once in ReceivableResolver, and once as a
        /// SQL expression inside GetPerformanceAsync, because aggregating in the database is what
        /// stopped the endpoint running out of memory on a real month. Two definitions of money can
        /// disagree, and this is what stops them.
        /// </para>
        /// <para>
        /// Every combination of refund, payday and bill state goes through the query, and the
        /// expected total is computed by the resolver itself. Change one side alone and this fails.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData(false, false, null, null, null)]
        [InlineData(false, true, null, null, null)]
        [InlineData(true, true, null, null, null)]
        [InlineData(false, false, BillStatus.Paid, 700.0, 900.0)]
        [InlineData(false, false, BillStatus.Paid, 0.0, 900.0)]
        [InlineData(false, false, BillStatus.Paid, 0.0, 0.0)]
        [InlineData(false, true, BillStatus.Registered, null, 900.0)]
        [InlineData(false, false, BillStatus.Registered, null, 0.0)]
        [InlineData(true, true, BillStatus.Paid, 700.0, 900.0)]
        public async Task TheQueryAndTheResolver_AgreeOnWhatWasReceived(
            bool refunded, bool hasPayday, BillStatus? billStatus, double? billPaid, double? billAmount)
        {
            const decimal total = 1000m;
            var order = Sale(total, payday: hasPayday ? new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc) : null, refunded: refunded);

            var bills = billStatus.HasValue
                ? new[]
                {
                    new Bill
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        Status = billStatus.Value,
                        PaidAmount = billPaid.HasValue ? (decimal)billPaid.Value : null,
                        Amount = billAmount.HasValue ? (decimal)billAmount.Value : 0m,
                        IsActive = true,
                        CreatedAt = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc)
                    }
                }
                : Array.Empty<Bill>();

            Given([order], bills, commissionRate: null, target: null);

            var performance = await BuildService().GetPerformanceAsync(EnterpriseId, 2026, 8);

            var expectedReceived = ReceivableResolver.ResolveReceivedAmount(
                total,
                ReceivableResolver.IsOrderRefunded(order.RefundedAt, order.Status),
                hasPayday,
                billStatus,
                billPaid.HasValue ? (decimal)billPaid.Value : null,
                billAmount.HasValue ? (decimal)billAmount.Value : null);

            var expectedSold = refunded ? 0m : total;

            Assert.Equal(expectedReceived, performance.Enterprise.ReceivedAmount);
            Assert.Equal(expectedSold, performance.Enterprise.SoldAmount);
            Assert.Equal(refunded ? 0 : 1, performance.Enterprise.OrderCount);
        }
    }
}
