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
            IQueryable<Enterprise> query = _context.Set<Enterprise>().Where(e => e.Id == id).AsNoTracking();
            Enterprise? enterprise = await query.FirstOrDefaultAsync();

            if (enterprise == null)
                throw new NotFoundDatabaseException();

            return enterprise;
        }

        public async override Task<IEnumerable<Enterprise>> GetAllAsync(Func<Enterprise, bool>? predicate)
        {
            IQueryable<Enterprise> query = _context.Set<Enterprise>().AsNoTracking();
            IEnumerable<Enterprise> result = predicate != null
                ? query.AsEnumerable().Where(predicate).ToList()
                : await query.ToListAsync();

            if (!result.Any())
                throw new NotFoundDatabaseException();

            return result;
        }

        public async override Task<IEnumerable<Enterprise>> GetAllAsync(int? pageNumber, int? pageSize, Func<Enterprise, bool>? predicate = null)
        {
            if (pageNumber == null || pageSize == null)
                return await GetAllAsync(predicate);

            IQueryable<Enterprise> query = _context.Set<Enterprise>().AsNoTracking();
            int skip = (pageNumber.Value - 1) * pageSize.Value;

            IEnumerable<Enterprise> result = predicate != null
                ? query.AsEnumerable().Where(predicate).Skip(skip).Take(pageSize.Value).ToList()
                : await query.Skip(skip).Take(pageSize.Value).ToListAsync();

            if (!result.Any())
                throw new NotFoundDatabaseException();

            return result;
        }

        public async Task<IEnumerable<Enterprise>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Enterprise enterprise
        )
        {
            if (enterprise == null)
                throw new NotFoundDatabaseException("Empty filter with no data found.");

            Expression<Func<Enterprise, object>> orderBy = FillOrderByPerField(enterprise);

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

        private static Expression<Func<Enterprise, object>> FillOrderByPerField(Enterprise enterprise)
        {
            if (enterprise.Id != Guid.Empty)
                return x => x.Id;
            else if (!string.IsNullOrEmpty(enterprise.Name))
                return x => x.Name;
            else if (!string.IsNullOrEmpty(enterprise.Email))
                return x => x.Email;
            else if (!string.IsNullOrEmpty(enterprise.Adress))
                return x => x.Adress;
            else if (!string.IsNullOrEmpty(enterprise.PhoneNumber))
                return x => x.PhoneNumber;
            else if (enterprise.CreatedAt != DateTime.MinValue)
                return x => x.CreatedAt;
            else if (enterprise.UpdatedAt.HasValue && enterprise.UpdatedAt.Value != DateTime.MinValue)
                return x => x.UpdatedAt ?? DateTime.MinValue;

            throw new NotFoundDatabaseException("Empty filter with no data found.");
        }
    }
}
