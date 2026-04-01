using EvangelionERPV2.AuditModule.Application.Interface;
using EvangelionERPV2.AuditModule.Domain.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.AuditModule.Application.Services
{
    public class AuditTrailService : IAuditTrailService
    {
        private const int DefaultPageNumber = 1;
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;

        private readonly IAuditTrailRepository _auditTrailRepository;

        public AuditTrailService(IAuditTrailRepository auditTrailRepository)
        {
            _auditTrailRepository = auditTrailRepository;
        }

        public Task<AuditTrail?> GetByIdAsync(Guid id, Guid enterpriseId)
        {
            return _auditTrailRepository.GetByIdAsync(id, enterpriseId);
        }

        public Task<(IEnumerable<AuditTrail> AuditTrails, int TotalItems)> GetAllAsyncFiltering(
            Guid enterpriseId,
            bool descending,
            int? pageNumber,
            int? pageSize,
            AuditTrailFilterDTO? filter = null)
        {
            int resolvedPageNumber = ResolvePageNumber(pageNumber);
            int resolvedPageSize = ResolvePageSize(pageSize);

            return _auditTrailRepository.GetAllAsyncFiltering(
                enterpriseId,
                descending,
                resolvedPageNumber,
                resolvedPageSize,
                filter);
        }

        private static int ResolvePageNumber(int? pageNumber)
        {
            if (!pageNumber.HasValue || pageNumber.Value <= 0)
                return DefaultPageNumber;

            return pageNumber.Value;
        }

        private static int ResolvePageSize(int? pageSize)
        {
            if (!pageSize.HasValue || pageSize.Value <= 0)
                return DefaultPageSize;

            return Math.Min(pageSize.Value, MaxPageSize);
        }
    }
}
