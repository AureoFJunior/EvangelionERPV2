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
using Serilog;

namespace EvangelionERPV2.ProductModule.Domain.Repositories
{
    public class ProductRepository : Repository<Product>, IProductRepository<Product>
    {
        private const string CacheKeyPrefix = "Product:v3:";

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
                try
                {
                    var productFromCache = JsonSerializer.Deserialize<Product>(cachedProduct);
                    if (productFromCache != null)
                    {
                        NormalizePictureAddress(productFromCache);
                        return productFromCache;
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Warning(
                        ex,
                        "Product cache payload is invalid and will be ignored. CacheKey={CacheKey} ErrorType={ErrorType}",
                        cacheKey,
                        ex.GetType().Name);
                }
            }

            IQueryable<Product> query = _context.Set<Product>().Where(e => e.Id == id).AsNoTracking();

            Product? product = await query.FirstOrDefaultAsync();
            if (product == null)
                throw new NotFoundDatabaseException();

            NormalizePictureAddress(product);
            await SetCachedProduct(cacheKey, product);

            return product;
        }

        public override Product Update(Product entity)
        {
            RemoveCachedProduct(entity.Id);
            return base.Update(entity);
        }

        public override IEnumerable<Product> UpdateRange(IEnumerable<Product> entitys)
        {
            foreach (var entity in entitys)
                RemoveCachedProduct(entity.Id);

            return base.UpdateRange(entitys);
        }

        private async Task SetCachedProduct(string cacheKey, Product product)
        {
            try
            {
                var productToCache = JsonSerializer.Serialize(product);

                await _cache.SetStringAsync(cacheKey, productToCache, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
                });
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(
                    ex,
                    "Unable to set product cache entry. CacheKey={CacheKey} ErrorType={ErrorType}",
                    cacheKey,
                    ex.GetType().Name);
            }
        }

        public async Task<Product> GetFreshByIdAsync(Guid id)
        {
            var product = await _context.Set<Product>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id)
                ?? throw new NotFoundDatabaseException();
            NormalizePictureAddress(product);
            return product;
        }

        private async Task<Tuple<string, string?>> GetCachedProductById(Guid id)
        {
            string cacheKey = $"{CacheKeyPrefix}{id}";

            try
            {
                string? cachedProduct = await _cache.GetStringAsync(cacheKey);
                return Tuple.Create(cacheKey, cachedProduct);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(
                    ex,
                    "Unable to read product cache entry. CacheKey={CacheKey} ErrorType={ErrorType}",
                    cacheKey,
                    ex.GetType().Name);
            }

            return Tuple.Create(cacheKey, (string?)null);
        }

        public async override Task<IEnumerable<Product>> GetAllAsync(Expression<Func<Product, bool>>? predicate)
        {
            IQueryable<Product> query = _context.Set<Product>().AsNoTracking();

            if (predicate != null)
                query = query.Where(predicate);

            IEnumerable<Product> result = await query.ToListAsync();

            if (!result.Any())
                throw new NotFoundDatabaseException();

            foreach (var item in result)
                NormalizePictureAddress(item);

            return result;
        }

        public async override Task<IEnumerable<Product>> GetAllAsync(int? pageNumber, int? pageSize, Expression<Func<Product, bool>>? predicate = null)
        {
            if (pageNumber == null || pageSize == null)
                return await GetAllAsync(predicate);

            IQueryable<Product> query = _context.Set<Product>().AsNoTracking();

            if (predicate != null)
                query = query.Where(predicate);

            int skip = (pageNumber.Value - 1) * pageSize.Value;

            IEnumerable<Product> result = await query
                .OrderBy(entity => entity.Id)
                .Skip(skip)
                .Take(pageSize.Value)
                .ToListAsync();

            if (!result.Any())
                throw new NotFoundDatabaseException();

            foreach (var item in result)
                NormalizePictureAddress(item);

            return result;
        }

        public void RemoveCachedProduct(Guid id)
        {
            if (id == Guid.Empty)
                return;

            var cacheKey = $"{CacheKeyPrefix}{id}";
            try
            {
                _cache.Remove(cacheKey);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(
                    ex,
                    "Unable to remove product cache entry. CacheKey={CacheKey} ErrorType={ErrorType}",
                    cacheKey,
                    ex.GetType().Name);
            }
        }

        private static void NormalizePictureAddress(Product product)
        {
            product.PictureAdress = SharedFunctions.EnsureDecryptedAddress(product.PictureAdress);
        }

        public async Task<(IEnumerable<Product>, int)> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Product product
        )
        {
            if (product == null)
                throw new NotFoundDatabaseException("Empty filter with no data found.");

            if (product.Id != Guid.Empty)
            {
                string? cacheKey, cachedProduct = "";
                (cacheKey, cachedProduct) = await GetCachedProductById(product.Id);

                if (!string.IsNullOrEmpty(cachedProduct))
                {
                    try
                    {
                        var productFromCache = JsonSerializer.Deserialize<Product>(cachedProduct);
                        if (productFromCache != null)
                        {
                            NormalizePictureAddress(productFromCache);
                            return (new List<Product>() { productFromCache }, 1);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Warning(
                            ex,
                            "Product cache payload is invalid and will be ignored. CacheKey={CacheKey} ErrorType={ErrorType}",
                            cacheKey,
                            ex.GetType().Name);
                    }
                }
            }

            Expression<Func<Product, object>> orderBy = FillOrderByPerField(product);

            var nameFilter = SharedFunctions.EnsureSearchFilterLength(product.Name, 150, nameof(product.Name));
            var escapedNameFilter = SharedFunctions.EscapeLikePattern(nameFilter ?? string.Empty);

            (var products, int totalItems) = await GetAllAsyncByFilterWithCountInternal(
                descending,
                pageNumber,
                pageSize,
                x =>
                (string.IsNullOrEmpty(nameFilter) || EF.Functions.Like(x.Name, $"%{escapedNameFilter}%", "\\"))
                && (x.EnterpriseId != null && (product.EnterpriseId == x.EnterpriseId && x.EnterpriseId != default(Guid))),
                orderBy
            );

            var productList = products.ToList();
            foreach (var item in productList)
                NormalizePictureAddress(item);

            foreach (var item in productList.Take(10))
            {
                await SetCachedProduct($"{CacheKeyPrefix}{item.Id}", item);
            }

            return (productList, totalItems);
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
