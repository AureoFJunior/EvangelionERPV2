using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.AuditModule.Application.Interface
{
    public interface IAuditTrailService
    {
        Task<(IEnumerable<AuditTrail> AuditTrails, int TotalItems)> GetAllAsyncFiltering(
            Guid enterpriseId,
            bool descending,
            int? pageNumber,
            int? pageSize,
            AuditTrailFilterDTO? filter = null);

        Task<AuditTrail?> GetByIdAsync(Guid id, Guid enterpriseId);

        Task<int> DeleteOlderThanAsync(
            Guid enterpriseId,
            DateTime cutoffDateUtc,
            CancellationToken cancellationToken = default);
    }
}
