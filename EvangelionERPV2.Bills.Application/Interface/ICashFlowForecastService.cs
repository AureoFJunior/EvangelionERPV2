using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface ICashFlowForecastService
    {
        Task<CashFlowForecastDTO> GetForecastAsync(Guid enterpriseId, int horizonInDays, double? currentBalanceOverride = null);
        Task<IEnumerable<SimulationResultDTO>> RunSimulationAsync(Guid enterpriseId, Guid userId, RunSimulationRequestDTO request);
    }
}
