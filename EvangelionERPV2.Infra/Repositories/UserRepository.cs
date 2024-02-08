using EvangelionERPV2.Domain.Exceptions;
using EvangelionERPV2.Domain.Models;
using EvangelionERPV2.Infra.Context;
using Microsoft.EntityFrameworkCore;


namespace EvangelionERPV2.Infra.Repositories
{
    public class UserRepository : Repository<User>
    {
        public UserRepository(AppDbContext context) : base(context)
        {
        }

        public async override Task<User> GetByIdAsync(Guid id)
        {
            try
            {
                var query = _context.Set<User>().Where(e => e.Id == id).AsNoTracking();

                if (await query.AnyAsync())
                    return await query.FirstOrDefaultAsync();

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }

        public async override Task<IEnumerable<User>> GetAllAsync(Func<User, bool> predicate)
        {
            try
            {
                var query = _context.Set<User>().AsNoTracking().Where(predicate).AsQueryable();

                if (await query.AnyAsync())
                    return await query.ToListAsync();

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }
    }
}