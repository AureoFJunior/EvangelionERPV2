using System.Linq.Expressions;
using EvangelionERPV2.OrderModule.Application.Services;
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

        private SalesPerformanceService BuildService() =>
            new(_orders.Object, _bills.Object, _users.Object, _goals.Object);

        private void Given(IEnumerable<Order> orders, IEnumerable<Bill> bills, decimal? commissionRate, decimal? target)
        {
            _orders.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>?>())).ReturnsAsync(orders);
            _bills.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Bill, bool>>?>())).ReturnsAsync(bills);
            _users.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<User, bool>>?>())).ReturnsAsync(new[]
            {
                new User { Id = SellerId, EnterpriseId = EnterpriseId, IsActive = true, FirstName = "Rei", LastName = "Ayanami", CommissionRate = commissionRate }
            });
            _goals.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<SalesGoal, bool>>?>())).ReturnsAsync(
                target.HasValue
                    ? new[] { new SalesGoal { Id = Guid.NewGuid(), EnterpriseId = EnterpriseId, UserId = SellerId, Year = 2026, Month = 8, TargetAmount = target.Value, IsActive = true } }
                    : Array.Empty<SalesGoal>());
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
            // The two screens are read by two people looking at the same commission. They share the
            // calculation precisely so they cannot disagree.
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
            // OrderRepository.GetAllAsync throws NotFoundDatabaseException on an empty result and
            // that maps to 204; this service reads through the un-overridden generic repository for
            // exactly that reason. A quiet month must render as zeros.
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

            _orders.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>?>()))
                .ReturnsAsync([Sale(1000m), robotSale]);
            _bills.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Bill, bool>>?>())).ReturnsAsync([]);
            _goals.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<SalesGoal, bool>>?>())).ReturnsAsync([]);
            GivenFilteredUsers(Person(SellerId), Robot(robotId));

            var performance = await BuildService().GetPerformanceAsync(EnterpriseId, 2026, 8);

            Assert.Equal([SellerId], performance.Sellers.Select(row => row.UserId).ToArray());
            // The money was real even though the seller was not a person, so the company keeps it.
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

            // The seller's and the company's own, never the robot's — a target set before the flag
            // existed must not keep the account visible on the metas screen.
            Assert.Equal(2, goals.Count);
            Assert.DoesNotContain(goals, goal => goal.UserId == robotId);
        }
    }
}
