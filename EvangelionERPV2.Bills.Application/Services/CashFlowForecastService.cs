using System.Text.Json;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    public class CashFlowForecastService : ICashFlowForecastService
    {
        private const int MaxSimulationScenarioNameLength = 100;

        // decimal has no NaN/Infinity, so an explicit bound replaces the old double.IsFinite guard.
        private const decimal MaxPayableMultiplier = 1000m;

        // Unbounded balances overflow the running-balance accumulation in BuildForecastAsync
        // (decimal.MaxValue + anything throws). 1e15 is far above any real enterprise balance
        // and leaves headroom for 90 days of projected movement. Public so the controller can
        // reject the same range on the balance-override endpoint.
        public const decimal MaxCurrentBalance = 1_000_000_000_000_000m;
        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IRepository<Bill> _billRepository;
        private readonly IRepository<Enterprise> _enterpriseRepository;
        private readonly IRepository<ForecastSimulationLog> _simulationLogRepository;

        public CashFlowForecastService(
            IRepository<Order> orderRepository,
            IRepository<PayableBill> payableBillRepository,
            IRepository<Bill> billRepository,
            IRepository<Enterprise> enterpriseRepository,
            IRepository<ForecastSimulationLog> simulationLogRepository)
        {
            _orderRepository = orderRepository;
            _payableBillRepository = payableBillRepository;
            _billRepository = billRepository;
            _enterpriseRepository = enterpriseRepository;
            _simulationLogRepository = simulationLogRepository;
        }

        public async Task<CashFlowForecastDTO> GetForecastAsync(Guid enterpriseId, int horizonInDays, decimal? currentBalanceOverride = null)
        {
            return await BuildForecastAsync(enterpriseId, horizonInDays, currentBalanceOverride, 0, 1);
        }

        public async Task<IEnumerable<SimulationResultDTO>> RunSimulationAsync(Guid enterpriseId, Guid userId, RunSimulationRequestDTO request)
        {
            if (request == null)
                throw new ArgumentException("Invalid simulation request.");

            if (request.HorizonInDays is not (30 or 60 or 90))
                throw new ArgumentException("Horizon must be 30, 60 or 90 days.");

            if (request.CurrentBalance is < -MaxCurrentBalance or > MaxCurrentBalance)
                throw new ArgumentException("Invalid simulation request.");

            var scenarios = request.Scenarios?.ToList() ?? [];
            if (scenarios.Count < 2)
                throw new ArgumentException("At least two scenarios are required.");

            foreach (var scenario in scenarios)
            {
                if (scenario == null)
                    throw new ArgumentException("Invalid simulation request.");

                if (string.IsNullOrWhiteSpace(scenario.ScenarioName))
                    throw new ArgumentException("Invalid simulation request.");

                if (scenario.ScenarioName.Length > MaxSimulationScenarioNameLength)
                    throw new ArgumentException("Invalid simulation request.");

                if (scenario.PayableMultiplier is < 0 or > MaxPayableMultiplier)
                    throw new ArgumentException("Invalid simulation request.");
            }

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

        private async Task<CashFlowForecastDTO> BuildForecastAsync(Guid enterpriseId, int horizonInDays, decimal? currentBalanceOverride, int receivableDelayInDays, decimal payableMultiplier)
        {
            var today = DateTime.UtcNow.Date;
            var endDate = today.AddDays(horizonInDays);

            var orders = (await _orderRepository.GetAllAsync(x => x.EnterpriseId == enterpriseId && x.IsActive == true)).ToList();
            var payables = (await _payableBillRepository.GetAllAsync(x => x.EnterpriseId == enterpriseId && x.IsActive == true)).ToList();
            var enterprise = await _enterpriseRepository.GetByIdAsync(enterpriseId);
            var currentBalance = currentBalanceOverride ?? enterprise?.CurrentBalance ?? 0;

            var forecastOrders = orders
                .Where(x => !ReceivableResolver.IsOrderRefunded(x) && x.TotalValue > 0)
                .ToList();

            // Where an order has a boleto, the boleto is the truth about the receivable: the order's
            // own dates only ever said when we *hoped* to be paid. Cancelled titles are skipped so a
            // reissued order resolves to the one the payer is actually holding. Scoped by order id
            // because Bill carries no EnterpriseId of its own.
            var orderIds = forecastOrders.Select(x => x.Id).ToList();
            var billsByOrder = ReceivableResolver.SelectLiveBillsByOrder(
                await _billRepository.GetAllAsync(
                    x => orderIds.Contains(x.OrderId)
                         && (x.IsActive == null || x.IsActive == true)
                         && x.Status != BillStatus.Cancelled));

            var receivables = forecastOrders
                .Select(x => new
                {
                    Date = ReceivableResolver.ResolveOrderReceivableDate(x, billsByOrder.GetValueOrDefault(x.Id), today)
                        ?.AddDays(receivableDelayInDays),
                    Amount = ReceivableResolver.ResolveReceivableAmount(x, billsByOrder.GetValueOrDefault(x.Id))
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

        private static bool IsPayableRefunded(PayableBill payableBill)
        {
            return payableBill.RefundedAt.HasValue;
        }
    }
}
