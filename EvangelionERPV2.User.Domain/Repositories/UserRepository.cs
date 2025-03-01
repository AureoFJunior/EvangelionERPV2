using Microsoft.EntityFrameworkCore;
using EvangelionERPV2.UserModule.Infra.Context;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.UserModule.Domain.Repositories
{
    public class UserRepository : Repository<User>
    {
        public UserRepository(UserModuleDbContext context) : base(context)
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
                IQueryable<User> query = _context.Set<User>().AsNoTracking().AsQueryable();

                if (predicate != null)
                    return _context.Set<Shared.Entities.User>().AsNoTracking().Where(predicate);

                if (await query.AnyAsync())
                    return await query.ToListAsync();

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }
    }
}