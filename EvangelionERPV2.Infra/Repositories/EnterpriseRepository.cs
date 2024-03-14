using EvangelionERPV2.Domain.Exceptions;
using EvangelionERPV2.Domain.Interfaces.Repositories;
using EvangelionERPV2.Domain.Models;
using EvangelionERPV2.Infra.Context;
using Microsoft.EntityFrameworkCore;


namespace EvangelionERPV2.Infra.Repositories
{
    public class EnterpriseRepository : Repository<Enterprise>, IEnterpriseRepository<Enterprise>
    {
        public EnterpriseRepository(AppDbContext context) : base(context)
        {
        }

        public async override Task<Enterprise> GetByIdAsync(Guid id)
        {
            try
            {
                var query = _context.Set<Enterprise>().Where(e => e.Id == id).AsNoTracking();

                if (await query.AnyAsync())
                    return await query.FirstOrDefaultAsync();

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }

        public async override Task<IEnumerable<Enterprise>> GetAllAsync(Func<Enterprise, bool> predicate)
        {
            try
            {
                var query = _context.Set<Enterprise>().AsNoTracking();

                if (await query.AnyAsync())
                    return await query.ToListAsync();

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }

        public async override Task<IEnumerable<Enterprise>> GetAllAsync(int? pageNumber, int? pageSize, Func<Enterprise, bool> predicate = null)
        {
            try
            {
                if(pageNumber == null || pageSize == null)
                    return await this.GetAllAsync(predicate);

                var query = _context.Set<Enterprise>().AsNoTracking();

                int skip = (pageNumber - 1) * pageSize ?? 1;
                List<Enterprise>? result = null;

                if (await query.AnyAsync())
                    result = await query.Skip(skip).Take(pageSize ?? 0).ToListAsync();

                if (result?.Any() == false)
                    return result;

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }
    }
}