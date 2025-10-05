using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.OrderModule.Infra.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using System.Linq.Expressions;

namespace EvangelionERPV2.OrderModule.Domain.Repositories
{
    public class OrderRepository : Repository<Order>, IOrderRepository<Order>
    {
        private readonly IDistributedCache _cache;
        public OrderRepository(OrderModuleDbContext context, IDistributedCache cache) : base(context)
        {
            _cache = cache;
        }

        public async override Task<Order> GetByIdAsync(Guid id)
        {
            string? cacheKey, cachedItem = "";
            (cacheKey, cachedItem) = await GetCachedOrderById(id);

            if (!string.IsNullOrEmpty(cachedItem))
            {
                // Deserialize the cached product and return it
                var orderFromCache = JsonConvert.DeserializeObject<Order>(cachedItem);
                return orderFromCache;
            }

            // If not found in cache, query the database
            IQueryable<Order> query = _context.Set<Order>().Where(e => e.Id == id).AsNoTracking();

            if (await query.AnyAsync())
            {
                Order order = await query.FirstOrDefaultAsync();

                await SetCachedOrder(cacheKey, order);

                return order;
            }

            throw new NotFoundDatabaseException();
        }

        public async override Task<IEnumerable<Order>> GetAllAsync(Func<Order, bool> predicate)
        {
            IQueryable<Order> query = _context.Set<Order>().AsNoTracking();
            IEnumerable<Order> result = new List<Order>();

            result = await query.ToListAsync();
            if (predicate != null)
                result = result.Where(predicate).ToList();

            if (result?.Any() == true)
                return result;

            throw new NotFoundDatabaseException();
        }

        public async override Task<IEnumerable<Order>> GetAllAsync(int? pageNumber, int? pageSize, Func<Order, bool> predicate = null)
        {
            if (pageNumber == null || pageSize == null)
                return await GetAllAsync(predicate);

            IQueryable<Order> query = _context.Set<Order>().AsNoTracking();
            IEnumerable<Order> result = Enumerable.Empty<Order>();

            if (predicate != null)
                result = query.Where(predicate);

            int skip = (pageNumber - 1) * pageSize ?? 1;

            result = await query.Skip(skip).Take(pageSize ?? 0).ToListAsync();
            if (result?.Any() == true)
                return result;

            throw new NotFoundDatabaseException();
        }

        public async Task<IEnumerable<Order>> GetAllAsyncWithOrderedProductsByEnterprise(Enterprise? enterprise)
        {
            IEnumerable<Order> result = Enumerable.Empty<Order>();

            var query = _context.Set<Order>()
                .Include(o => o.OrderedProduct)
                .Where(x => x.IsActive ?? false 
                    && (x.PaymentScheduledDate.IsDateBetween(SharedFunctions.GetFirstDayOfMonth(), SharedFunctions.GetLastDayOfMonth())
                    || x.Payday != null && x.Payday.IsDateBetween(SharedFunctions.GetFirstDayOfMonth(), SharedFunctions.GetLastDayOfMonth()))
                    && (x.EnterpriseId != null && enterprise.Id == (x.EnterpriseId ?? new Guid())))
                .AsNoTracking();

            result = await query.ToListAsync();

            if (result?.Any() == true)
                return result;

            throw new NotFoundDatabaseException();
        }

        public async Task<(IEnumerable<Order>, int)> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Order order
        )
        {
            Expression<Func<Order, object>> orderBy = null;

            if (order == null)
                throw new NotFoundDatabaseException("Empty filter with no data found.");

            orderBy = FillOrderByPerField(order, orderBy);
            int totalItems = 0;
            IEnumerable<Order> orders = Enumerable.Empty<Order>();

            (orders, totalItems) = await this.GetAllAsyncByFilter(
            descending,
            pageNumber,
            pageSize,
            null,
            orderBy
            );

            return (orders, totalItems);
        }

        private static Expression<Func<Order, object>> FillOrderByPerField(Order order, Expression<Func<Order, object>> orderBy)
        {
            if (order.Id != null && order.Id != Guid.Empty)
                return x => x.Id;
            else if (order.TotalValue > 0)
                return x => x.TotalValue;
            else if (order.CustomerId != null)
                return x => x.CustomerId;
            else if (order.Enterprise != null)
                return x => x.Enterprise;
            else if (order.Payday != null && order.Payday != DateTime.MinValue)
                return x => x.Payday;
            else if (order.PaymentScheduledDate != null && order.PaymentScheduledDate != DateTime.MinValue)
                return x => x.Payday;
            else if (order.CreatedAt != null && order.CreatedAt != DateTime.MinValue)
                return x => x.CreatedAt;
            else if (order.UpdatedAt != null && order.UpdatedAt != DateTime.MinValue)
                return x => x.UpdatedAt;

            throw new NotFoundDatabaseException("Empty filter with no data found.");
        }

        private async Task SetCachedOrder(string cacheKey, Order order)
        {
            // Serialize the product and store it in Redis cache for future use
            var orderToCache = JsonConvert.SerializeObject(order);

            await _cache.SetStringAsync(cacheKey, orderToCache, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60) // Cache for 60 minutes
            });
        }

        private async Task<Tuple<string, string?>> GetCachedOrderById(Guid id)
        {
            // Define the cache key using the order ID
            string cacheKey = $"Order:{id}";

            // Try to get the order from Redis cache
            string? cachedItem = await _cache.GetStringAsync(cacheKey);

            return Tuple.Create(cacheKey, cachedItem);
        }
    }
}