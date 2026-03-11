using EvangelionERPV2.AuditModule.Domain.Interface;
using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.AuditModule.Domain.Repositories
{
    public class AuditTrailRepository : IAuditTrailRepository
    {
        private readonly AppDbContext _context;

        public AuditTrailRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<(IEnumerable<AuditTrail> AuditTrails, int TotalItems)> GetAllAsyncFiltering(
            bool descending,
            int? pageNumber,
            int? pageSize,
            AuditTrailFilterDTO? filter = null)
        {
            IQueryable<AuditTrail> query = _context.Set<AuditTrail>()
                .AsNoTracking()
                .Include(x => x.User)
                .Where(x => AuditedEntities.Contains(x.EntityName));

            query = ApplyFilter(query, filter);
            query = descending ? query.OrderByDescending(x => x.ChangedAt) : query.OrderBy(x => x.ChangedAt);

            var totalItems = await query.CountAsync();
            if (pageNumber.HasValue && pageSize.HasValue && pageNumber > 0 && pageSize > 0)
            {
                int skip = (pageNumber.Value - 1) * pageSize.Value;
                query = query.Skip(skip).Take(pageSize.Value);
            }

            var result = await query.ToListAsync();
            return (result, totalItems);
        }

        public async Task<AuditTrail?> GetByIdAsync(Guid id)
        {
            if (id == Guid.Empty)
                return null;

            return await _context.Set<AuditTrail>()
                .AsNoTracking()
                .Include(x => x.User)
                .FirstOrDefaultAsync(x => x.Id == id && AuditedEntities.Contains(x.EntityName));
        }

        private static IQueryable<AuditTrail> ApplyFilter(IQueryable<AuditTrail> query, AuditTrailFilterDTO? filter)
        {
            if (filter == null)
                return query;

            if (filter.UserId.HasValue && filter.UserId != Guid.Empty)
                query = query.Where(x => x.UserId == filter.UserId.Value);

            if (!string.IsNullOrWhiteSpace(filter.UserName))
            {
                var userName = filter.UserName.Trim();
                query = query.Where(x => x.User != null && x.User.UserName.Contains(userName));
            }

            if (!string.IsNullOrWhiteSpace(filter.EntityName))
            {
                var entityName = filter.EntityName.Trim();
                query = query.Where(x => x.EntityName == entityName);
            }

            if (filter.EntityId.HasValue && filter.EntityId != Guid.Empty)
                query = query.Where(x => x.EntityId == filter.EntityId.Value);

            if (!string.IsNullOrWhiteSpace(filter.Action))
            {
                var action = filter.Action.Trim().ToUpperInvariant();
                query = query.Where(x => x.Action == action);
            }

            if (filter.ChangedFrom.HasValue)
            {
                var changedFrom = NormalizeToUtc(filter.ChangedFrom.Value);
                query = query.Where(x => x.ChangedAt >= changedFrom);
            }

            if (filter.ChangedTo.HasValue)
            {
                var changedTo = NormalizeToUtc(filter.ChangedTo.Value);
                query = query.Where(x => x.ChangedAt <= changedTo);
            }

            return query;
        }

        private static DateTime NormalizeToUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }
    }
}
