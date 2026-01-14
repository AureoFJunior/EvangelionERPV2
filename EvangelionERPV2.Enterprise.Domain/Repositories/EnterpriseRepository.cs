using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.EnterpriseModule.Domain.Repositories
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
                IQueryable<Enterprise> query = _context.Set<Enterprise>().Where(e => e.Id == id).AsNoTracking();

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
                IQueryable<Enterprise> query = _context.Set<Enterprise>().AsNoTracking();
                if (predicate != null)
                    return _context.Set<Enterprise>().AsNoTracking().Where(predicate);

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
                if (pageNumber == null || pageSize == null)
                    return await GetAllAsync(predicate);

                IQueryable<Enterprise> query = _context.Set<Enterprise>().AsNoTracking();
                int skip = (pageNumber - 1) * pageSize ?? 1;

                if (predicate != null)
                    return _context.Set<Enterprise>().AsNoTracking().Where(predicate).Skip(skip).Take(pageSize ?? 0);

                List<Enterprise>? result = null;

                if (await query.AnyAsync())
                    result = await query.Skip(skip).Take(pageSize ?? 0).ToListAsync();

                if (result == null || result?.Any() == false)
                    return result;

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }

        public async Task<IEnumerable<Enterprise>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Enterprise enterprise
        )
        {
            try
            {
                Expression<Func<Enterprise, object>> orderBy = null;

                if (enterprise == null)
                    throw new NotFoundDatabaseException("Empty filter with no data found.");

                orderBy = FillOrderByPerField(enterprise, orderBy);

                return await this.GetAllAsyncByFilter(
                descending,
                pageNumber,
                pageSize,
                x =>
                string.IsNullOrEmpty(enterprise.Name) || x.Name == enterprise.Name
                ,
                orderBy
                );
            }
            catch (Exception ex) { throw; }
        }

        private static Expression<Func<Enterprise, object>> FillOrderByPerField(Enterprise enterprise, Expression<Func<Enterprise, object>> orderBy)
        {
            if (enterprise.Id != null && enterprise.Id != Guid.Empty)
                return x => x.Id;
            else if (!string.IsNullOrEmpty(enterprise.Name))
                return x => x.Name;
            else if (!string.IsNullOrEmpty(enterprise.Email))
                return x => x.Email;
            else if (!string.IsNullOrEmpty(enterprise.Adress))
                return x => x.Adress;
            else if (!string.IsNullOrEmpty(enterprise.PhoneNumber))
                return x => x.PhoneNumber;
            else if (enterprise.CreatedAt != null && enterprise.CreatedAt != DateTime.MinValue)
                return x => x.CreatedAt;
            else if (enterprise.UpdatedAt != null && enterprise.UpdatedAt != DateTime.MinValue)
                return x => x.UpdatedAt;

            throw new NotFoundDatabaseException("Empty filter with no data found.");
        }
    }
}
