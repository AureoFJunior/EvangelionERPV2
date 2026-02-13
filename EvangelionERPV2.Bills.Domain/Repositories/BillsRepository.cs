using EvangelionERPV2.BillsModule.Domain.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.BillsModule.Domain.Repositories
{
    public class BillsRepository : Repository<Bill>, IBillsRepository<Bill>
    {
        public BillsRepository(AppDbContext context) : base(context)
        {
        }

        public async Task<Bill?> GetByOrderIdAsync(Guid orderId)
        {
            return await _context.Set<Bill>()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.OrderId == orderId && (x.IsActive == null || x.IsActive == true));
        }
    }
}

