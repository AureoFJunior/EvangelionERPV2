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

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(orderRepo.Object, payableRepo.Object, logRepo.Object);

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

            var logRepo = new Mock<IRepository<ForecastSimulationLog>>();
            var service = new CashFlowForecastService(orderRepo.Object, payableRepo.Object, logRepo.Object);

            await Assert.ThrowsAsync<ArgumentException>(() => service.RunSimulationAsync(Guid.NewGuid(), Guid.NewGuid(), new RunSimulationRequestDTO
            {
                HorizonInDays = 30,
                CurrentBalance = 100,
                Scenarios = [new ForecastSimulationScenarioDTO { ScenarioName = "A" }]
            }));
        }
    }
}
