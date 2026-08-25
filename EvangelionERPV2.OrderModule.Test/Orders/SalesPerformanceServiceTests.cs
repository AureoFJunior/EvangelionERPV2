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
    }
}
