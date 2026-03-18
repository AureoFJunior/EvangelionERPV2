using System.Text.Json;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    public class CashFlowForecastService : ICashFlowForecastService
    {
        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IRepository<Enterprise> _enterpriseRepository;
        private readonly IRepository<ForecastSimulationLog> _simulationLogRepository;

        public CashFlowForecastService(
            IRepository<Order> orderRepository,
            IRepository<PayableBill> payableBillRepository,
            IRepository<Enterprise> enterpriseRepository,
            IRepository<ForecastSimulationLog> simulationLogRepository)
        {
            _orderRepository = orderRepository;
            _payableBillRepository = payableBillRepository;
            _enterpriseRepository = enterpriseRepository;
            _simulationLogRepository = simulationLogRepository;
        }

        public async Task<CashFlowForecastDTO> GetForecastAsync(Guid enterpriseId, int horizonInDays, double? currentBalanceOverride = null)
        {
            return await BuildForecastAsync(enterpriseId, horizonInDays, currentBalanceOverride, 0, 1);
        }

        public async Task<IEnumerable<SimulationResultDTO>> RunSimulationAsync(Guid enterpriseId, Guid userId, RunSimulationRequestDTO request)
        {
            var scenarios = request.Scenarios?.ToList() ?? [];
            if (scenarios.Count < 2)
                throw new ArgumentException("At least two scenarios are required.");

            var baseline = await BuildForecastAsync(enterpriseId, request.HorizonInDays, request.CurrentBalance, 0, 1);
            var baselineCurrentBalance = baseline.CurrentBalance;
            var baselineFinalBalance = baseline.FinalProjectedBalance;
            var results = new List<SimulationResultDTO>();

            foreach (var scenario in scenarios)
            {
                var simulation = await BuildForecastAsync(
                    enterpriseId,
                    request.HorizonInDays,
                    baselineCurrentBalance,
                    scenario.ReceivableDelayInDays,
                    scenario.PayableMultiplier);

                var riskDays = simulation.DailyProjection.Where(x => x.IsRiskDay).Select(x => x.Date.Date).ToList();
                results.Add(new SimulationResultDTO
                {
                    ScenarioName = scenario.ScenarioName,
                    FinalProjectedBalance = simulation.FinalProjectedBalance,
                    Impact = simulation.FinalProjectedBalance - baselineFinalBalance,
                    RiskDays = riskDays
                });

                await _simulationLogRepository.CreateAsync(new ForecastSimulationLog
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    UserId = userId,
                    ExecutedAt = DateTime.UtcNow,
                    ScenarioName = scenario.ScenarioName,
                    HorizonInDays = request.HorizonInDays,
                    FinalProjectedBalance = simulation.FinalProjectedBalance,
                    InputsJson = JsonSerializer.Serialize(scenario),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }

            await _simulationLogRepository.CommitAsync();
            return results;
        }

        private async Task<CashFlowForecastDTO> BuildForecastAsync(Guid enterpriseId, int horizonInDays, double? currentBalanceOverride, int receivableDelayInDays, double payableMultiplier)
        {
            var today = DateTime.UtcNow.Date;
            var endDate = today.AddDays(horizonInDays);

            var orders = (await _orderRepository.GetAllAsync(x => x.EnterpriseId == enterpriseId && x.IsActive == true)).ToList();
            var payables = (await _payableBillRepository.GetAllAsync(x => x.EnterpriseId == enterpriseId && x.IsActive == true)).ToList();
            var enterprise = await _enterpriseRepository.GetByIdAsync(enterpriseId);
            var currentBalance = currentBalanceOverride ?? enterprise?.CurrentBalance ?? 0;

            var forecastOrders = orders
                .Where(x => !IsOrderRefunded(x) && x.TotalValue > 0)
                .ToList();

            var receivables = forecastOrders
                .Select(x => new
                {
                    Date = ResolveOrderReceivableDate(x, today)?.AddDays(receivableDelayInDays),
                    Amount = x.TotalValue
                })
                .Where(x => x.Date.HasValue)
                .Select(x => new
                {
                    Date = x.Date!.Value,
                    x.Amount
                })
                .Where(x => x.Date >= today && x.Date <= endDate)
                .GroupBy(x => x.Date)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.Amount));

            var forecastPayables = payables
                .Where(x => !IsPayableRefunded(x) && x.Amount > 0)
                .Where(x => !x.IsPaid || x.PaidAt.HasValue)
                .ToList();

            var projectedPayables = forecastPayables
                .Select(x => new
                {
                    Date = ResolvePayableDate(x, today),
                    Amount = x.Amount
                })
                .Where(x => x.Date.HasValue)
                .Select(x => new
                {
                    Date = x.Date!.Value,
                    x.Amount
                })
                .Where(x => x.Date >= today && x.Date <= endDate)
                .GroupBy(x => x.Date)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.Amount * payableMultiplier));

            var projection = new List<CashFlowForecastDayDTO>();
            var runningBalance = currentBalance;

            for (var date = today; date <= endDate; date = date.AddDays(1))
            {
                receivables.TryGetValue(date, out var dayReceivable);
                projectedPayables.TryGetValue(date, out var dayPayable);
                runningBalance += dayReceivable - dayPayable;

                projection.Add(new CashFlowForecastDayDTO
                {
                    Date = date,
                    AccountsReceivable = dayReceivable,
                    AccountsPayable = dayPayable,
                    ProjectedBalance = runningBalance,
                    IsRiskDay = runningBalance < 0
                });
            }

            return new CashFlowForecastDTO
            {
                HorizonInDays = horizonInDays,
                CurrentBalance = currentBalance,
                FinalProjectedBalance = runningBalance,
                DailyProjection = projection
            };
        }

        private static DateTime? ResolveOrderReceivableDate(Order order, DateTime today)
        {
            if (order.Payday.HasValue)
            {
                var paidDate = order.Payday.Value.Date;
                return paidDate >= today ? paidDate : null;
            }

            var scheduledDate = order.PaymentScheduledDate.Date;
            // When using persisted current balance, realized receivables in the past must not be projected again.
            return scheduledDate < today ? null : scheduledDate;
        }

        private static DateTime? ResolvePayableDate(PayableBill payableBill, DateTime today)
        {
            if (payableBill.IsPaid)
            {
                if (!payableBill.PaidAt.HasValue)
                    return null;

                var paidDate = payableBill.PaidAt.Value.Date;
                return paidDate >= today ? paidDate : null;
            }

            var dueDate = payableBill.DueDate.Date;
            return dueDate < today ? today : dueDate;
        }

        private static bool IsOrderRefunded(Order order)
        {
            return order.RefundedAt.HasValue || order.Status == (int)EnumOrderStatus.Refund;
        }

        private static bool IsPayableRefunded(PayableBill payableBill)
        {
            return payableBill.RefundedAt.HasValue;
        }
    }
}
