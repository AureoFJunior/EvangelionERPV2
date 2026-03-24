using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.ReportsModule.Application.Interface
{
    public interface IReportsService
    {
        Task<IEnumerable<ReportListItemDTO>> GetUserReportsAsync(Guid enterpriseId, Guid userId);
        Task<ReportListItemDTO> GenerateAsync(Guid enterpriseId, Guid userId, EnumReportType type);
        Task<ReportDetailDTO?> GetByIdAsync(Guid enterpriseId, Guid userId, Guid reportId);
    }
}
