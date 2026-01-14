using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Repositories;


namespace EvangelionERPV2.ProductModule.Domain.Repositories
{
    public class OrderedProductRepository : Repository<OrderedProduct>, IOrderedProductRepository<OrderedProduct>
    {
        public OrderedProductRepository(AppDbContext context) : base(context)
        {
        }

        public async override Task<OrderedProduct> GetByIdAsync(Guid id)
        {
            try
            {
                IQueryable<OrderedProduct> query = _context.Set<OrderedProduct>().Where(e => e.Id == id).AsNoTracking();

                if (await query.AnyAsync())
                    return await query.FirstOrDefaultAsync();

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }

        public async override Task<IEnumerable<OrderedProduct>> GetAllAsync(Func<OrderedProduct, bool> predicate)
        {
            try
            {
                IQueryable<OrderedProduct> query = _context.Set<OrderedProduct>().AsNoTracking();

                if (predicate != null)
                    return query.Where(predicate);

                if (await query.AnyAsync())
                    return await query.ToListAsync();

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }

        public async override Task<IEnumerable<OrderedProduct>> GetAllAsync(int? pageNumber, int? pageSize, Func<OrderedProduct, bool> predicate = null)
        {
            try
            {
                if (pageNumber == null || pageSize == null)
                    return await GetAllAsync(predicate);

                IQueryable<OrderedProduct> query = _context.Set<OrderedProduct>().AsNoTracking();
                int skip = (pageNumber - 1) * pageSize ?? 1;

                if (predicate != null)
                    return _context.Set<OrderedProduct>().AsNoTracking().Where(predicate).Skip(skip).Take(pageSize ?? 0);

                List<OrderedProduct>? result = null;

                if (query.Any())
                    result = await query.Skip(skip).Take(pageSize ?? 0).ToListAsync();

                if (result == null || result?.Any() == false)
                    return result;

                throw new NotFoundDatabaseException();
            }
            catch (Exception ex) { throw; }
        }
    }
}
