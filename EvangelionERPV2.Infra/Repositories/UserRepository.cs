using EvangelionERPV2.Domain.Exceptions;
using EvangelionERPV2.Domain.Models;
using EvangelionERPV2.Domain.Utils;
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

        public async override Task<IEnumerable<User>> GetAllAsync()
        {
            try
            {
                var query = _context.Set<User>().AsNoTracking();

                if (await query.AnyAsync())
                    return await query.ToListAsync();

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }
    }
}