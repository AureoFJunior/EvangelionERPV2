using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Linq.Expressions;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Repositories;
using System.Text.Json;

namespace EvangelionERPV2.ProductModule.Domain.Repositories
{
    public class ProductRepository : Repository<Product>, IProductRepository<Product>
    {
        private readonly IDistributedCache _cache;

        public ProductRepository(AppDbContext context, IDistributedCache cache) : base(context)
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
                var productFromCache = JsonSerializer.Deserialize<Product>(cachedProduct);
                if (productFromCache != null)
                    return productFromCache;
            }

            // If not found in cache, query the database

            IQueryable<Product> query = _context.Set<Product>().Where(e => e.Id == id).AsNoTracking();

            Product? product = await query.FirstOrDefaultAsync();
            if (product == null)
                throw new NotFoundDatabaseException();

            await SetCachedProduct(cacheKey, product);

            return product;
        }

        private async Task SetCachedProduct(string cacheKey, Product product)
        {
            // Decrypt the PictureAddress field
            product.PictureAdress = SharedFunctions.Decrypt(product.PictureAdress ?? "");

            // Serialize the product and store it in Redis cache for future use
            var productToCache = JsonSerializer.Serialize(product);

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

        public async override Task<IEnumerable<Product>> GetAllAsync(Func<Product, bool>? predicate)
        {
            IEnumerable<Product> result = Enumerable.Empty<Product>();
            if (predicate != null)
                result = _context.Set<Product>().AsNoTracking().Where(predicate).ToList();
            else
                result = await _context.Set<Product>().AsNoTracking().ToListAsync();

            if (!result.Any())
                throw new NotFoundDatabaseException();

            foreach (var item in result)
                item.PictureAdress = SharedFunctions.Decrypt(item.PictureAdress ?? "");

            return result;
        }

        public async override Task<IEnumerable<Product>> GetAllAsync(int? pageNumber, int? pageSize, Func<Product, bool>? predicate = null)
        {

            if (pageNumber == null || pageSize == null)
                return await GetAllAsync(predicate);

            int skip = (pageNumber.Value - 1) * pageSize.Value;
            IEnumerable<Product> result;

            if (predicate != null)
            {
                result = _context.Set<Product>()
                    .AsNoTracking()
                    .AsEnumerable()
                    .Where(predicate)
                    .Skip(skip)
                    .Take(pageSize.Value)
                    .ToList();
            }
            else
            {
                result = await _context.Set<Product>()
                    .AsNoTracking()
                    .Skip(skip)
                    .Take(pageSize.Value)
                    .ToListAsync();
            }

            if (!result.Any())
                throw new NotFoundDatabaseException();

            foreach (var item in result)
                item.PictureAdress = SharedFunctions.Decrypt(item.PictureAdress ?? "");

            return result;
        }

        public async Task<(IEnumerable<Product>, int)> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Product product
        )
        {
            if (product == null)
                throw new NotFoundDatabaseException("Empty filter with no data found.");

            if (product.Id != Guid.Empty) // Filter in cache by ID to optimize query time
            {
                string? cacheKey, cachedProduct = "";
                (cacheKey, cachedProduct) = await GetCachedProductById(product.Id);

                if (!string.IsNullOrEmpty(cachedProduct))
                {
                    // Deserialize the cached product and return it
                    var productFromCache = JsonSerializer.Deserialize<Product>(cachedProduct);
                    if (productFromCache != null)
                        return (new List<Product>() { productFromCache }, 1);
                }
            }

            Expression<Func<Product, object>> orderBy = FillOrderByPerField(product);

            var nameFilter = product.Name?.Trim();

            (var products, int totalItems) = await GetAllAsyncByFilterWithCountInternal(
                descending,
                pageNumber,
                pageSize,
                x =>
                (string.IsNullOrEmpty(nameFilter) || EF.Functions.Like(x.Name, $"%{nameFilter}%"))
                && (x.EnterpriseId != null && (product.EnterpriseId == x.EnterpriseId && x.EnterpriseId != default(Guid))),
                orderBy
            );

            if (products.Any())
                foreach (var item in products.Take(10).ToList()) // Save the first 10 itens when not found the exact searched product
                {
                    await SetCachedProduct($"Product:{item.Name}", item);
                }

            return (products, totalItems);
        }

        private static Expression<Func<Product, object>> FillOrderByPerField(Product product)
        {
            if (product.Id != Guid.Empty)
                return x => x.Id;
            else if (!string.IsNullOrEmpty(product.Name))
                return x => x.Name;
            else if (product.DefaultValue > 0)
                return x => x.DefaultValue;
            else if (product.CreatedAt != DateTime.MinValue)
                return x => x.CreatedAt;
            else if (product.UpdatedAt.HasValue && product.UpdatedAt.Value != DateTime.MinValue)
                return x => x.UpdatedAt ?? DateTime.MinValue;

            throw new NotFoundDatabaseException("Empty filter with no data found.");
        }
    }
}
