using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.ProductModule.Infra.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System.Linq.Expressions;

namespace EvangelionERPV2.ProductModule.Domain.Repositories
{
    public class ProductRepository : Repository<Product>, IProductRepository<Product>
    {
        private readonly IDistributedCache _cache;

        public ProductRepository(ProductModuleDbContext context, IDistributedCache cache) : base(context)
        {
            _cache = cache;
        }

        public async override Task<Product> GetByIdAsync(Guid id)
        {
            string? cacheKey, cachedProduct = "";
            (cacheKey, cachedProduct) = await GetCachedProductById(id);

            if (!string.IsNullOrEmpty(cachedProduct))
            {
                // Deserialize the cached product and return it
                var productFromCache = JsonConvert.DeserializeObject<Product>(cachedProduct);
                return productFromCache;
            }

            // If not found in cache, query the database
            IQueryable<Product> query = _context.Set<Product>().Where(e => e.Id == id).AsNoTracking();

            if (await query.AnyAsync())
            {
                Product product = await query.FirstOrDefaultAsync();

                await SetCachedProduct(cacheKey, product);

                return product;
            }

            throw new NotFoundDatabaseException();
        }

        private async Task SetCachedProduct(string cacheKey, Product product)
        {
            // Decrypt the PictureAddress field
            product.PictureAdress = SharedFunctions.Decrypt(product.PictureAdress ?? "");

            // Serialize the product and store it in Redis cache for future use
            var productToCache = JsonConvert.SerializeObject(product);

            await _cache.SetStringAsync(cacheKey, productToCache, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60) // Cache for 60 minutes
            });
        }

        private async Task<Tuple<string, string?>> GetCachedProductById(Guid id)
        {
            // Define the cache key using the product ID
            string cacheKey = $"Product:{id}";

            // Try to get the product from Redis cache
            string? cachedProduct = await _cache.GetStringAsync(cacheKey);

            return Tuple.Create(cacheKey, cachedProduct);
        }

        private async Task<Tuple<string, string?>> GetCachedProductByName(string name)
        {
            // Define the cache key using the product name
            string cacheKey = $"Product:{name}";

            // Try to get the product from Redis cache
            string? cachedProduct = await _cache.GetStringAsync(cacheKey);

            return Tuple.Create(cacheKey, cachedProduct);
        }

        public async override Task<IEnumerable<Product>> GetAllAsync(Func<Product, bool> predicate)
        {
            IQueryable<Product> query;
            IEnumerable<Product> result = Enumerable.Empty<Product>();
            if (predicate != null)
                result = _context.Set<Product>().AsNoTracking().Where(predicate).ToList();
            else
                result = await _context.Set<Product>().AsNoTracking().ToListAsync();

            if (result.Count() > 0)
            {
                foreach (var item in result)
                    item.PictureAdress = SharedFunctions.Decrypt(item.PictureAdress ?? "");

                return result;
            }

            throw new NotFoundDatabaseException();
        }

        public async override Task<IEnumerable<Product>> GetAllAsync(int? pageNumber, int? pageSize, Func<Product, bool> predicate = null)
        {

            if (pageNumber == null || pageSize == null)
                return await GetAllAsync(predicate);

            IQueryable<Product> query = _context.Set<Product>().AsNoTracking();
            int skip = (pageNumber - 1) * pageSize ?? 1;

            if (predicate == null)
                return _context.Set<Product>().AsNoTracking().Where(predicate).Skip(skip).Take(pageSize ?? 0);

            List<Product>? result = null;

            if (await query.AnyAsync())
                result = await query.Skip(skip).Take(pageSize ?? 0).ToListAsync();

            if (result?.Any() == false)
                return result;

            throw new NotFoundDatabaseException();
        }

        public async Task<(IEnumerable<Product>, int)> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Product product
        )
        {
            Expression<Func<Product, object>> orderBy = null;

            if (product == null)
                throw new NotFoundDatabaseException("Empty filter with no data found.");

            if (!string.IsNullOrEmpty(product.Name)) // Filter in cache by name to optimize query time
            {
                string? cacheKey, cachedProduct = "";
                (cacheKey, cachedProduct) = await GetCachedProductByName(product.Name);

                if (!string.IsNullOrEmpty(cachedProduct))
                {
                    // Deserialize the cached product and return it
                    var productFromCache = JsonConvert.DeserializeObject<Product>(cachedProduct);
                    return (new List<Product>() { productFromCache }, 1);
                }
            }

            orderBy = FillOrderByPerField(product, orderBy);

            (var products, int totalItems) = await this.GetAllAsyncByFilter(
            descending,
            pageNumber,
            pageSize,
            x =>
            string.IsNullOrEmpty(product.Name) || x.Name == product.Name
            ,
            orderBy
            );

            if (products.Any())
                foreach (var item in products.Take(10).ToList()) // Save the first 10 itens when not found the exact searched product
                {
                    await SetCachedProduct($"Product:{item.Name}", item);
                }

            return (products, totalItems);
        }

        private static Expression<Func<Product, object>> FillOrderByPerField(Product product, Expression<Func<Product, object>> orderBy)
        {
            if (product.Id != null && product.Id != Guid.Empty)
                return x => x.Id;
            else if (!string.IsNullOrEmpty(product.Name))
                return x => x.Name;
            else if (product.DefaultValue > 0)
                return x => x.DefaultValue;
            else if (product.CreatedAt != null && product.CreatedAt != DateTime.MinValue)
                return x => x.CreatedAt;
            else if (product.UpdatedAt != null && product.UpdatedAt != DateTime.MinValue)
                return x => x.UpdatedAt;

            throw new NotFoundDatabaseException("Empty filter with no data found.");
        }
    }
}