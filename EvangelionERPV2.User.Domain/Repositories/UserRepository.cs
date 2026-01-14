using Microsoft.EntityFrameworkCore;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.UserModule.Domain.Repositories
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
                var query = _context.Set<User>()
                    .Include(o => o.Enterprise)
                    .Where(e => e.Id == id).AsNoTracking();

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
                IQueryable<User> query = _context.Set<User>()
                    .Include(o => o.Enterprise)
                    .AsNoTracking().AsQueryable();

                if (predicate != null)
                    return _context.Set<Shared.Entities.User>()
                         .Include(o => o.Enterprise)
                         .AsNoTracking()
                         .Where(predicate);

                if (await query.AnyAsync())
                    return await query.ToListAsync();

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }

        public override IEnumerable<User> GetByCondition(Func<User, bool> condition)
        {
            try
            {
                var query = _context.Set<User>()
                     .Include(o => o.Enterprise)
                     .AsNoTracking()
                     .Where(condition);

                if (query.Any())
                    return query.ToList();

                return new List<User>();
            }
            catch (Exception ex) { throw; }
        }
    }
}
