using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
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
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Func<Order, bool>>())).ReturnsAsync(new List<Order>());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Func<PayableBill, bool>>())).ReturnsAsync(new List<PayableBill>());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(enterpriseId)).ReturnsAsync(
                new Enterprise { Id = enterpriseId, IsActive = true, Name = "E", CurrentBalance = 321.45 });

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(orderRepo.Object, payableRepo.Object, enterpriseRepo.Object, logRepo.Object);

            var result = await service.GetForecastAsync(enterpriseId, 5);

            Assert.Equal(321.45, result.CurrentBalance, 2);
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
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Func<Order, bool>>())).ReturnsAsync((Func<Order, bool>? f) => orders.Where(f!).ToList());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Func<PayableBill, bool>>())).ReturnsAsync((Func<PayableBill, bool>? f) => payables.Where(f!).ToList());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(enterpriseId)).ReturnsAsync(new Enterprise { Id = enterpriseId, CurrentBalance = 0, IsActive = true, Name = "E" });

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(orderRepo.Object, payableRepo.Object, enterpriseRepo.Object, logRepo.Object);

            var result = await service.GetForecastAsync(enterpriseId, 30, 100);

            Assert.Equal(0, result.FinalProjectedBalance, 2);
            Assert.Contains(result.DailyProjection, x => x.Date == today.AddDays(2) && x.IsRiskDay);
        }

        [Fact]
        public async Task RunSimulationAsync_WithLessThanTwoScenarios_ShouldThrow()
        {
            var orderRepo = new Mock<IRepository<Order>>();
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Func<Order, bool>>())).ReturnsAsync(new List<Order>());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Func<PayableBill, bool>>())).ReturnsAsync(new List<PayableBill>());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync(new Enterprise { Id = Guid.NewGuid(), CurrentBalance = 0, IsActive = true, Name = "E" });

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(orderRepo.Object, payableRepo.Object, enterpriseRepo.Object, logRepo.Object);

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
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Func<Order, bool>>())).ReturnsAsync((Func<Order, bool>? f) => orders.Where(f!).ToList());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Func<PayableBill, bool>>())).ReturnsAsync((Func<PayableBill, bool>? f) => payables.Where(f!).ToList());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(enterpriseId)).ReturnsAsync(new Enterprise { Id = enterpriseId, CurrentBalance = 0, IsActive = true, Name = "E" });

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(orderRepo.Object, payableRepo.Object, enterpriseRepo.Object, logRepo.Object);

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
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Func<Order, bool>>())).ReturnsAsync((Func<Order, bool>? f) => orders.Where(f!).ToList());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Func<PayableBill, bool>>())).ReturnsAsync((Func<PayableBill, bool>? f) => payables.Where(f!).ToList());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(enterpriseId)).ReturnsAsync(new Enterprise { Id = enterpriseId, CurrentBalance = 0, IsActive = true, Name = "E" });

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(orderRepo.Object, payableRepo.Object, enterpriseRepo.Object, logRepo.Object);

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
            orderRepo.Setup(x => x.GetAllAsync(It.IsAny<Func<Order, bool>>())).ReturnsAsync((Func<Order, bool>? f) => orders.Where(f!).ToList());

            var payableRepo = new Mock<IRepository<PayableBill>>();
            payableRepo.Setup(x => x.GetAllAsync(It.IsAny<Func<PayableBill, bool>>())).ReturnsAsync((Func<PayableBill, bool>? f) => payables.Where(f!).ToList());

            var enterpriseRepo = new Mock<IRepository<Enterprise>>();
            enterpriseRepo.Setup(x => x.GetByIdAsync(enterpriseId)).ReturnsAsync(new Enterprise { Id = enterpriseId, CurrentBalance = 0, IsActive = true, Name = "E" });

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(orderRepo.Object, payableRepo.Object, enterpriseRepo.Object, logRepo.Object);

            var result = await service.GetForecastAsync(enterpriseId, 30, 100);

            var day2 = result.DailyProjection.Single(x => x.Date == today.AddDays(2));
            var day3 = result.DailyProjection.Single(x => x.Date == today.AddDays(3));

            Assert.Equal(40, day2.AccountsPayable, 2);
            Assert.Equal(60, day2.ProjectedBalance, 2);
            Assert.Equal(0, day3.AccountsPayable, 2);
            Assert.Equal(60, day3.ProjectedBalance, 2);
            Assert.Equal(60, result.FinalProjectedBalance, 2);
        }

    }
}
