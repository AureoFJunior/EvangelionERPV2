using System.Linq.Expressions;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Moq;

namespace EvangelionERPV2.BillsModule.Test
{
    public class CashFlowForecastServiceTests
    {
        [Fact]
        public async Task GetForecastAsync_WhenOverrideIsNotProvided_ShouldUsePersistedEnterpriseBalance()
        {
            var enterpriseId = Guid.NewGuid();

            var orderRepo = new Mock<IRepository<Order>>();
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>())).ReturnsAsync(new List<Order>());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>())).ReturnsAsync(new List<PayableBill>());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(enterpriseId)).ReturnsAsync(
                new Enterprise { Id = enterpriseId, IsActive = true, Name = "E", CurrentBalance = 321.45m });

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(payableRepo.Object, enterpriseRepo.Object, logRepo.Object, BuildContext());

            var result = await service.GetForecastAsync(enterpriseId, 5);

            Assert.Equal(321.45m, result.CurrentBalance, 2);
        }

        [Fact]
        public async Task GetForecastAsync_ShouldCombineReceivablesAndPayablesAndFlagRiskDays()
        {
            var enterpriseId = Guid.NewGuid();
            var today = DateTime.UtcNow.Date;

            var orders = new List<Order>
            {
                new() { EnterpriseId = enterpriseId, PaymentScheduledDate = today.AddDays(1), TotalValue = 100, IsActive = true },
                new() { EnterpriseId = enterpriseId, PaymentScheduledDate = today.AddDays(3), TotalValue = 50, IsActive = true }
            };

            var payables = new List<PayableBill>
            {
                new() { EnterpriseId = enterpriseId, DueDate = today.AddDays(2), Amount = 250, IsPaid = false, IsActive = true }
            };

            var orderRepo = new Mock<IRepository<Order>>();
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>())).ReturnsAsync((Expression<Func<Order, bool>>? f) => orders.Where(f!.Compile()).ToList());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>())).ReturnsAsync((Expression<Func<PayableBill, bool>>? f) => payables.Where(f!.Compile()).ToList());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(enterpriseId)).ReturnsAsync(new Enterprise { Id = enterpriseId, CurrentBalance = 0, IsActive = true, Name = "E" });

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(payableRepo.Object, enterpriseRepo.Object, logRepo.Object, BuildContext(orders));

            var result = await service.GetForecastAsync(enterpriseId, 30, 100);

            Assert.Equal(0, result.FinalProjectedBalance, 2);
            Assert.Contains(result.DailyProjection, x => x.Date == today.AddDays(2) && x.IsRiskDay);
        }

        [Fact]
        public async Task RunSimulationAsync_WithLessThanTwoScenarios_ShouldThrow()
        {
            var orderRepo = new Mock<IRepository<Order>>();
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>())).ReturnsAsync(new List<Order>());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>())).ReturnsAsync(new List<PayableBill>());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync(new Enterprise { Id = Guid.NewGuid(), CurrentBalance = 0, IsActive = true, Name = "E" });

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(payableRepo.Object, enterpriseRepo.Object, logRepo.Object, BuildContext());

            await Assert.ThrowsAsync<ArgumentException>(() => service.RunSimulationAsync(Guid.NewGuid(), Guid.NewGuid(), new RunSimulationRequestDTO
            {
                HorizonInDays = 30,
                CurrentBalance = 100,
                Scenarios = [new ForecastSimulationScenarioDTO { ScenarioName = "A" }]
            }));
        }

        [Fact]
        public async Task GetForecastAsync_ShouldUsePaymentAndPaidDatesForReceivablesAndPayables()
        {
            var enterpriseId = Guid.NewGuid();
            var today = DateTime.UtcNow.Date;

            var orders = new List<Order>
            {
                new() { EnterpriseId = enterpriseId, PaymentScheduledDate = today.AddDays(4), TotalValue = 100, IsActive = true },
                new() { EnterpriseId = enterpriseId, PaymentScheduledDate = today.AddDays(8), Payday = today.AddDays(2), TotalValue = 200, IsActive = true }
            };

            var payables = new List<PayableBill>
            {
                new() { EnterpriseId = enterpriseId, DueDate = today.AddDays(3), Amount = 50, IsPaid = false, IsActive = true },
                new() { EnterpriseId = enterpriseId, DueDate = today.AddDays(10), PaidAt = today.AddDays(1), Amount = 80, IsPaid = true, IsActive = true }
            };

            var orderRepo = new Mock<IRepository<Order>>();
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>())).ReturnsAsync((Expression<Func<Order, bool>>? f) => orders.Where(f!.Compile()).ToList());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>())).ReturnsAsync((Expression<Func<PayableBill, bool>>? f) => payables.Where(f!.Compile()).ToList());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(enterpriseId)).ReturnsAsync(new Enterprise { Id = enterpriseId, CurrentBalance = 0, IsActive = true, Name = "E" });

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(payableRepo.Object, enterpriseRepo.Object, logRepo.Object, BuildContext(orders));

            var result = await service.GetForecastAsync(enterpriseId, 30, 0);

            var day1 = result.DailyProjection.Single(x => x.Date == today.AddDays(1));
            var day2 = result.DailyProjection.Single(x => x.Date == today.AddDays(2));
            var day3 = result.DailyProjection.Single(x => x.Date == today.AddDays(3));
            var day4 = result.DailyProjection.Single(x => x.Date == today.AddDays(4));

            Assert.Equal(80, day1.AccountsPayable, 2);
            Assert.Equal(-80, day1.ProjectedBalance, 2);

            Assert.Equal(200, day2.AccountsReceivable, 2);
            Assert.Equal(120, day2.ProjectedBalance, 2);

            Assert.Equal(50, day3.AccountsPayable, 2);
            Assert.Equal(70, day3.ProjectedBalance, 2);

            Assert.Equal(100, day4.AccountsReceivable, 2);
            Assert.Equal(170, day4.ProjectedBalance, 2);
            Assert.Equal(170, result.FinalProjectedBalance, 2);
        }

        [Fact]
        public async Task GetForecastAsync_ShouldProjectOverdueUnpaidBillsForTodayAndIgnorePastPaidBills()
        {
            var enterpriseId = Guid.NewGuid();
            var today = DateTime.UtcNow.Date;

            var orders = new List<Order>();
            var payables = new List<PayableBill>
            {
                new() { EnterpriseId = enterpriseId, DueDate = today.AddDays(-2), Amount = 120, IsPaid = false, IsActive = true },
                new() { EnterpriseId = enterpriseId, DueDate = today.AddDays(-4), PaidAt = today.AddDays(-1), Amount = 80, IsPaid = true, IsActive = true }
            };

            var orderRepo = new Mock<IRepository<Order>>();
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>())).ReturnsAsync((Expression<Func<Order, bool>>? f) => orders.Where(f!.Compile()).ToList());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>())).ReturnsAsync((Expression<Func<PayableBill, bool>>? f) => payables.Where(f!.Compile()).ToList());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(enterpriseId)).ReturnsAsync(new Enterprise { Id = enterpriseId, CurrentBalance = 0, IsActive = true, Name = "E" });

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(payableRepo.Object, enterpriseRepo.Object, logRepo.Object, BuildContext(orders));

            var result = await service.GetForecastAsync(enterpriseId, 30, 200);

            var day0 = result.DailyProjection.Single(x => x.Date == today);
            Assert.Equal(120, day0.AccountsPayable, 2);
            Assert.Equal(80, day0.ProjectedBalance, 2);
            Assert.Equal(80, result.FinalProjectedBalance, 2);
        }

        [Fact]
        public async Task GetForecastAsync_ShouldIgnorePaidBillsWithoutPaidAtDate()
        {
            var enterpriseId = Guid.NewGuid();
            var today = DateTime.UtcNow.Date;

            var orders = new List<Order>();
            var payables = new List<PayableBill>
            {
                new() { EnterpriseId = enterpriseId, DueDate = today.AddDays(2), Amount = 40, IsPaid = false, IsActive = true },
                new() { EnterpriseId = enterpriseId, DueDate = today.AddDays(3), Amount = 90, IsPaid = true, PaidAt = null, IsActive = true }
            };

            var orderRepo = new Mock<IRepository<Order>>();
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>())).ReturnsAsync((Expression<Func<Order, bool>>? f) => orders.Where(f!.Compile()).ToList());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>())).ReturnsAsync((Expression<Func<PayableBill, bool>>? f) => payables.Where(f!.Compile()).ToList());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(enterpriseId)).ReturnsAsync(new Enterprise { Id = enterpriseId, CurrentBalance = 0, IsActive = true, Name = "E" });

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(payableRepo.Object, enterpriseRepo.Object, logRepo.Object, BuildContext(orders));

            var result = await service.GetForecastAsync(enterpriseId, 30, 100);

            var day2 = result.DailyProjection.Single(x => x.Date == today.AddDays(2));
            var day3 = result.DailyProjection.Single(x => x.Date == today.AddDays(3));

            Assert.Equal(40, day2.AccountsPayable, 2);
            Assert.Equal(60, day2.ProjectedBalance, 2);
            Assert.Equal(0, day3.AccountsPayable, 2);
            Assert.Equal(60, day3.ProjectedBalance, 2);
            Assert.Equal(60, result.FinalProjectedBalance, 2);
        }

        /// <summary>
        /// The gap this milestone closes: before payment state existed, printing a boleto made its
        /// due date look like cash in the bank, because Order.Payday is exactly the date fed to the
        /// boleto as its vencimento.
        /// </summary>
        [Fact]
        public async Task GetForecastAsync_WhenBoletoIsIssuedButNotPaid_ShouldNotCountItAsReceivedCash()
        {
            var enterpriseId = Guid.NewGuid();
            var today = DateTime.UtcNow.Date;
            var orderId = Guid.NewGuid();

            var orders = new List<Order>
            {
                new() { Id = orderId, EnterpriseId = enterpriseId, PaymentScheduledDate = today.AddDays(4), Payday = today.AddDays(3), TotalValue = 100, IsActive = true }
            };

            var openBoleto = new Bill
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                DueDate = today.AddDays(-2),
                Amount = 100,
                Status = BillStatus.Registered,
                IsActive = true
            };

            var result = await BuildForecastAsync(enterpriseId, orders, openBoleto);

            Assert.All(result.DailyProjection, day => Assert.Equal(0, day.AccountsReceivable, 2));
            Assert.Equal(0, result.FinalProjectedBalance, 2);
        }

        [Fact]
        public async Task GetForecastAsync_WhenBoletoIsSettled_ShouldProjectTheAmountTheBankCredited()
        {
            var enterpriseId = Guid.NewGuid();
            var today = DateTime.UtcNow.Date;
            var orderId = Guid.NewGuid();

            var orders = new List<Order>
            {
                new() { Id = orderId, EnterpriseId = enterpriseId, PaymentScheduledDate = today.AddDays(9), TotalValue = 100, IsActive = true }
            };

            var settled = new Bill
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                DueDate = today.AddDays(1),
                Amount = 100,
                Status = BillStatus.Paid,
                PaidAt = today.AddDays(2),
                PaidAmount = 112.50m,
                IsActive = true
            };

            var result = await BuildForecastAsync(enterpriseId, orders, settled);

            Assert.Equal(112.50m, result.DailyProjection.Single(x => x.Date == today.AddDays(2)).AccountsReceivable, 2);
            Assert.Equal(0, result.DailyProjection.Single(x => x.Date == today.AddDays(9)).AccountsReceivable, 2);
            Assert.Equal(112.50m, result.FinalProjectedBalance, 2);
        }

        /// <summary>
        /// A cancelled title is the superseded half of a reissue; the boleto that replaced it is the
        /// one the forecast must read, and it is a different row with a different due date.
        /// </summary>
        [Fact]
        public async Task GetForecastAsync_AfterAReissue_ShouldProjectTheReplacementDueDate()
        {
            var enterpriseId = Guid.NewGuid();
            var today = DateTime.UtcNow.Date;
            var orderId = Guid.NewGuid();

            var orders = new List<Order>
            {
                new() { Id = orderId, EnterpriseId = enterpriseId, PaymentScheduledDate = today.AddDays(2), TotalValue = 100, IsActive = true }
            };

            var superseded = new Bill
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                DueDate = today.AddDays(2),
                Amount = 100,
                Status = BillStatus.Cancelled,
                CreatedAt = today.AddDays(-1),
                IsActive = true
            };
            var replacement = new Bill
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                DueDate = today.AddDays(6),
                Amount = 100,
                Status = BillStatus.Registered,
                CreatedAt = today,
                IsActive = true
            };

            var result = await BuildForecastAsync(enterpriseId, orders, superseded, replacement);

            Assert.Equal(0, result.DailyProjection.Single(x => x.Date == today.AddDays(2)).AccountsReceivable, 2);
            Assert.Equal(100, result.DailyProjection.Single(x => x.Date == today.AddDays(6)).AccountsReceivable, 2);
        }

        /// <summary>
        /// The window filter is in SQL now, and the resolver truncates to a date. An order due at
        /// 23:00 on the last day of the horizon must still count.
        /// </summary>
        [Fact]
        public async Task GetForecastAsync_WhenReceivableFallsLateOnTheFinalDay_ShouldStillProjectIt()
        {
            var enterpriseId = Guid.NewGuid();
            var today = DateTime.UtcNow.Date;
            var lastDay = today.AddDays(30);

            var orders = new List<Order>
            {
                new() { EnterpriseId = enterpriseId, PaymentScheduledDate = lastDay.AddHours(23), TotalValue = 77, IsActive = true }
            };

            var result = await BuildForecastAsync(enterpriseId, orders);

            Assert.Equal(77, result.DailyProjection.Single(x => x.Date == lastDay).AccountsReceivable, 2);
            Assert.Equal(77, result.FinalProjectedBalance, 2);
        }

        /// <summary>
        /// The other half of the same bound: orders outside the horizon must not be read at all.
        /// </summary>
        [Fact]
        public async Task GetForecastAsync_ShouldIgnoreOrdersOutsideTheHorizon()
        {
            var enterpriseId = Guid.NewGuid();
            var today = DateTime.UtcNow.Date;

            var orders = new List<Order>
            {
                new() { EnterpriseId = enterpriseId, PaymentScheduledDate = today.AddDays(31), TotalValue = 500, IsActive = true },
                new() { EnterpriseId = enterpriseId, PaymentScheduledDate = today.AddDays(-1), TotalValue = 900, IsActive = true },
                new() { EnterpriseId = enterpriseId, PaymentScheduledDate = today.AddDays(5), TotalValue = 10, IsActive = true }
            };

            var result = await BuildForecastAsync(enterpriseId, orders);

            Assert.Equal(10, result.FinalProjectedBalance, 2);
            Assert.All(result.DailyProjection.Where(x => x.Date != today.AddDays(5)),
                day => Assert.Equal(0, day.AccountsReceivable, 2));
        }

        /// <summary>
        /// Another tenant's orders must never reach the projection.
        /// </summary>
        [Fact]
        public async Task GetForecastAsync_ShouldIgnoreAnotherEnterprisesOrders()
        {
            var enterpriseId = Guid.NewGuid();
            var today = DateTime.UtcNow.Date;

            var orders = new List<Order>
            {
                new() { EnterpriseId = enterpriseId, PaymentScheduledDate = today.AddDays(2), TotalValue = 40, IsActive = true },
                new() { EnterpriseId = Guid.NewGuid(), PaymentScheduledDate = today.AddDays(2), TotalValue = 5000, IsActive = true }
            };

            var result = await BuildForecastAsync(enterpriseId, orders);

            Assert.Equal(40, result.FinalProjectedBalance, 2);
        }

        private static async Task<CashFlowForecastDTO> BuildForecastAsync(
            Guid enterpriseId, List<Order> orders, params Bill[] bills)
        {
            var orderRepo = new Mock<IRepository<Order>>();
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Order, bool>>>()))
                .ReturnsAsync((Expression<Func<Order, bool>>? f) => orders.Where(f!.Compile()).ToList());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<PayableBill, bool>>>()))
                .ReturnsAsync(new List<PayableBill>());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(enterpriseId))
                .ReturnsAsync(new Enterprise { Id = enterpriseId, CurrentBalance = 0, IsActive = true, Name = "E" });

            var service = new CashFlowForecastService(
                payableRepo.Object,
                enterpriseRepo.Object,
                new Mock<IRepository<ForecastSimulationLog>>().Object,
                BuildContext(orders, bills));

            return await service.GetForecastAsync(enterpriseId, 30, 0);
        }

        /// <summary>
        /// The forecast reads orders and their bills through EF, not through the repositories, so
        /// they have to exist in a context rather than in a mock.
        /// </summary>
        private static AppDbContext BuildContext(
            IEnumerable<Order>? orders = null,
            IEnumerable<Bill>? bills = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"cash-flow-forecast-{Guid.NewGuid():N}")
                .Options;

            var context = new AppDbContext(
                options,
                httpContextAccessor: null,
                auditTrailEntryFactory: new NoopAuditTrailEntryFactory());

            foreach (var order in orders ?? [])
            {
                if (order.Id == Guid.Empty)
                    order.Id = Guid.NewGuid();

                context.Set<Order>().Add(order);
            }

            foreach (var bill in bills ?? [])
            {
                if (bill.Id == Guid.Empty)
                    bill.Id = Guid.NewGuid();

                context.Set<Bill>().Add(bill);
            }

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

        /// <summary>
        /// A bill repository holding <paramref name="bills"/>, filtered through the caller's
        /// predicate the same way EF would. No bills is the default: an order without a boleto must
        /// keep projecting off its own dates exactly as it did before payment state existed.
        /// </summary>
        private static IRepository<Bill> BillRepoReturning(params Bill[] bills)
        {
            var repo = new Mock<IRepository<Bill>>();
            repo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Bill, bool>>>()))
                .ReturnsAsync((Expression<Func<Bill, bool>>? filter) =>
                    filter == null ? bills : bills.Where(filter.Compile()).ToList());

            return repo.Object;
        }
    }
}
