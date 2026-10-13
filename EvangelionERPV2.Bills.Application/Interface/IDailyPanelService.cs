using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IDailyPanelService
    {
        Task<DailyPanelDTO> GetTodayAsync(Guid enterpriseId, CancellationToken cancellation = default);
    }
}
