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
            IQueryable<OrderedProduct> query = _context.Set<OrderedProduct>().Where(e => e.Id == id).AsNoTracking();
            OrderedProduct? orderedProduct = await query.FirstOrDefaultAsync();

            if (orderedProduct == null)
                throw new NotFoundDatabaseException();

            return orderedProduct;
        }

        public async override Task<IEnumerable<OrderedProduct>> GetAllAsync(Func<OrderedProduct, bool>? predicate)
        {
            IEnumerable<OrderedProduct> result;
            IQueryable<OrderedProduct> query = _context.Set<OrderedProduct>().AsNoTracking();

            if (predicate != null)
            {
                result = query.AsEnumerable().Where(predicate).ToList();
            }
            else
            {
                result = await query.ToListAsync();
            }

            if (!result.Any())
                throw new NotFoundDatabaseException();

            return result;
        }

        public async override Task<IEnumerable<OrderedProduct>> GetAllAsync(int? pageNumber, int? pageSize, Func<OrderedProduct, bool>? predicate = null)
        {
            if (pageNumber == null || pageSize == null)
                return await GetAllAsync(predicate);

            int skip = (pageNumber.Value - 1) * pageSize.Value;
            IEnumerable<OrderedProduct> result;
            IQueryable<OrderedProduct> query = _context.Set<OrderedProduct>().AsNoTracking();

            if (predicate != null)
            {
                result = query.AsEnumerable()
                    .Where(predicate)
                    .Skip(skip)
                    .Take(pageSize.Value)
                    .ToList();
            }
            else
            {
                result = await query
                    .Skip(skip)
                    .Take(pageSize.Value)
                    .ToListAsync();
            }

            if (!result.Any())
                throw new NotFoundDatabaseException();

            return result;
        }
    }
}
