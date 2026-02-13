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
            var query = _context.Set<User>()
                .Include(o => o.Enterprise)
                .Where(e => e.Id == id).AsNoTracking();

            User? user = await query.FirstOrDefaultAsync();
            if (user == null)
                throw new NotFoundDatabaseException();

            return user;
        }

        public async override Task<IEnumerable<User>> GetAllAsync(Func<User, bool>? predicate)
        {
            IQueryable<User> query = _context.Set<User>()
                .Include(o => o.Enterprise)
                .AsNoTracking();

            IEnumerable<User> result = predicate != null
                ? query.AsEnumerable().Where(predicate).ToList()
                : await query.ToListAsync();

            if (!result.Any())
                throw new NotFoundDatabaseException();

            return result;
        }

        public override IEnumerable<User> GetByCondition(Func<User, bool> condition)
        {
            var query = _context.Set<User>()
                 .Include(o => o.Enterprise)
                 .AsNoTracking()
                 .Where(condition);

            var result = query.ToList();
            return result.Count > 0 ? result : new List<User>();
        }
    }
}
