using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.NFeModule.Domain.Repositories
{
    public class NFeRepository : Repository<NFeDocument>, INFeRepository<NFeDocument>
    {
        public NFeRepository(AppDbContext context) : base(context)
        {
        }

        public async Task<NFeDocument?> GetByOrderIdAsync(Guid orderId, NFeDocumentType? type = null)
        {
            var query = _context.Set<NFeDocument>()
                .AsNoTracking()
                .Where(x => x.OrderId == orderId && (x.IsActive == null || x.IsActive == true));

            if (type.HasValue)
                query = query.Where(x => x.Type == type.Value);

            return await query.FirstOrDefaultAsync();
        }

        public async Task<NFeDocument?> GetByAccessKeyAsync(string accessKey)
        {
            if (string.IsNullOrWhiteSpace(accessKey))
                return null;

            return await _context.Set<NFeDocument>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.AccessKey == accessKey && (x.IsActive == null || x.IsActive == true));
        }
    }
}
