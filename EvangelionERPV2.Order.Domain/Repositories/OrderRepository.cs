using EvangelionERPV2.OrderModule.Domain.Interface;
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

namespace EvangelionERPV2.OrderModule.Domain.Repositories
{
    public class OrderRepository : Repository<Order>, IOrderRepository<Order>
    {
        // v2: money moved from double to decimal, so payloads cached under v1 are not readable.
        private const string CacheKeyPrefix = "Order:v2:";

        private readonly IDistributedCache _cache;
        public OrderRepository(AppDbContext context, IDistributedCache cache) : base(context)
        {
            _cache = cache;
        }

        public async override Task<Order> GetByIdAsync(Guid id)
        {
            string? cacheKey, cachedItem = "";
            (cacheKey, cachedItem) = await GetCachedOrderById(id);

            if (!string.IsNullOrEmpty(cachedItem))
            {
                try
                {
                    var orderFromCache = JsonSerializer.Deserialize<Order>(cachedItem);
                    if (orderFromCache != null)
                        return orderFromCache;
                }
                catch (Exception ex)
                {
                    // A payload written by an older schema must not break reads: fall through to the database.
                    Log.Logger.Warning(
                        ex,
                        "Order cache payload is invalid and will be ignored. CacheKey={CacheKey} ErrorType={ErrorType}",
                        cacheKey,
                        ex.GetType().Name);
                }
            }

            IQueryable<Order> query = _context.Set<Order>()
                .Include(o => o.Customer)
                .Include(o => o.OrderedProduct!)
                    .ThenInclude(op => op.Product)
                .Where(e => e.Id == id)
                .AsSplitQuery()
                .AsNoTracking();

            Order? order = await query.FirstOrDefaultAsync();
            if (order == null)
                throw new NotFoundDatabaseException();

            await SetCachedOrder(cacheKey, order);

            return order;
        }

        public async override Task<IEnumerable<Order>> GetAllAsync(Expression<Func<Order, bool>>? predicate)
        {
            IQueryable<Order> query = _context.Set<Order>()
                .Include(o => o.Customer)
                .Include(o => o.OrderedProduct!)
                    .ThenInclude(op => op.Product)
                .Where(o => o.IsActive ?? false)
                .AsSplitQuery()
                .AsNoTracking();

            if (predicate != null)
                query = query.Where(predicate);

            IEnumerable<Order> result = await query.ToListAsync();

            if (result?.Any() == true)
                return result;

            throw new NotFoundDatabaseException();
        }

        public async override Task<IEnumerable<Order>> GetAllAsync(int? pageNumber, int? pageSize, Expression<Func<Order, bool>>? predicate = null)
        {
            if (pageNumber == null || pageSize == null)
                return await GetAllAsync(predicate);

            IQueryable<Order> query = _context.Set<Order>()
                .Include(o => o.Customer)
                .Include(o => o.OrderedProduct!)
                    .ThenInclude(op => op.Product)
                .Where(o => o.IsActive ?? false)
                .AsSplitQuery()
                .AsNoTracking();

            if (predicate != null)
                query = query.Where(predicate);

            int skip = (pageNumber.Value - 1) * pageSize.Value;

            IEnumerable<Order> result = await query
                .OrderBy(entity => entity.Id)
                .Skip(skip)
                .Take(pageSize.Value)
                .ToListAsync();

            if (result?.Any() == true)
                return result;

            throw new NotFoundDatabaseException();
        }

        public async Task<IEnumerable<Order>> GetAllAsyncWithOrderedProductsByEnterprise(Enterprise? enterprise)
        {
            if (enterprise == null)
                throw new NotFoundDatabaseException("Enterprise not found.");

            IEnumerable<Order> result = Enumerable.Empty<Order>();

            var query = _context.Set<Order>()
                .Include(o => o.Customer)
                .Include(o => o.OrderedProduct!)
                    .ThenInclude(op => op.Product)
                .Where(x => x.IsActive ?? false 
                    && (x.PaymentScheduledDate.IsDateBetween(SharedFunctions.GetFirstDayOfMonth(), SharedFunctions.GetLastDayOfMonth())
                    || x.Payday != null && x.Payday.IsDateBetween(SharedFunctions.GetFirstDayOfMonth(), SharedFunctions.GetLastDayOfMonth()))
                    && (x.EnterpriseId != null && (enterprise.Id == x.EnterpriseId && x.EnterpriseId != default(Guid))))
                .AsSplitQuery()
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
            if (order == null)
                throw new NotFoundDatabaseException("Empty filter with no data found.");

            Expression<Func<Order, object>> orderBy = FillOrderByPerField(order);
            int totalItems = 0;
            IEnumerable<Order> orders = Enumerable.Empty<Order>();

            IQueryable<Order> query = _context.Set<Order>()
                .Include(o => o.Customer)
                .Include(o => o.OrderedProduct!)
                    .ThenInclude(op => op.Product)
                .AsSplitQuery()
                .AsNoTracking();

            if (order.Id != Guid.Empty)
                query = query.Where(x => x.Id == order.Id);

            if (order.CustomerId.HasValue && order.CustomerId.Value != Guid.Empty)
                query = query.Where(x => x.CustomerId == order.CustomerId);

            if (order.EnterpriseId.HasValue && order.EnterpriseId.Value != Guid.Empty)
                query = query.Where(x => x.EnterpriseId == order.EnterpriseId);

            if (order.TotalValue > 0)
                query = query.Where(x => x.TotalValue == order.TotalValue);

            if (order.IsActive != null)
                query = query.Where(x => x.IsActive == order.IsActive);
            else
                query = query.Where(x => x.IsActive ?? false);

            query = descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);

            totalItems = await query.CountAsync();

            if (pageNumber != null && pageSize != null)
            {
                int skip = (pageNumber.Value - 1) * pageSize.Value;
                orders = await query.Skip(skip).Take(pageSize.Value).ToListAsync();
            }
            else
            {
                orders = await query.ToListAsync();
            }

            return (orders, totalItems);
        }

        private static Expression<Func<Order, object>> FillOrderByPerField(Order order)
        {
            if (order.Id != Guid.Empty)
                return x => x.Id;
            else if (order.TotalValue > 0)
                return x => x.TotalValue;
            else if (order.CustomerId.HasValue)
                return x => x.CustomerId ?? Guid.Empty;
            else if (order.Enterprise != null)
                return x => x.EnterpriseId ?? Guid.Empty;
            else if (order.Payday.HasValue && order.Payday.Value != DateTime.MinValue)
                return x => x.Payday ?? DateTime.MinValue;
            else if (order.PaymentScheduledDate != DateTime.MinValue)
                return x => x.PaymentScheduledDate;
            else if (order.CreatedAt != DateTime.MinValue)
                return x => x.CreatedAt;
            else if (order.UpdatedAt.HasValue && order.UpdatedAt.Value != DateTime.MinValue)
                return x => x.UpdatedAt ?? DateTime.MinValue;

            throw new NotFoundDatabaseException("Empty filter with no data found.");
        }

        /// <summary>
        /// Every write drops the cached copy first.
        /// </summary>
        /// <remarks>
        /// <see cref="GetByIdAsync"/> serves from Redis for an hour, and nothing used to clear it:
        /// a deleted order kept answering 200 as if it were live, a refund read back unrefunded and
        /// an edit read back stale. Every write path in OrderService — update, soft delete, refund —
        /// ends at Update, so one override covers all three. The raw-SQL guards those paths run
        /// before it are unaffected; this only removes a key.
        /// </remarks>
        public override Order Update(Order entity)
        {
            InvalidateCachedOrder(entity?.Id);
            return base.Update(entity);
        }

        public override IEnumerable<Order> UpdateRange(IEnumerable<Order> entities)
        {
            var materialised = entities?.ToList() ?? [];
            foreach (var entity in materialised)
                InvalidateCachedOrder(entity?.Id);

            return base.UpdateRange(materialised);
        }

        public override Order Delete(Order entity)
        {
            InvalidateCachedOrder(entity?.Id);
            return base.Delete(entity);
        }

        /// <summary>
        /// Drops the key, and never fails the write for it: a cache that is down must not stop an
        /// order being saved. The cost of a missed removal is a stale read until the hour is up,
        /// which is exactly the state this method exists to end — so it is logged loudly.
        /// </summary>
        private void InvalidateCachedOrder(Guid? id)
        {
            if (id == null || id == Guid.Empty)
                return;

            try
            {
                _cache.Remove($"{CacheKeyPrefix}{id}");
            }
            catch (Exception ex)
            {
                Log.Logger.Error(
                    "Could not drop the cached order after a write; reads may be stale for up to an hour. OrderId={OrderId} ErrorType={ErrorType}",
                    id,
                    ex.GetType().Name);
            }
        }

        private async Task SetCachedOrder(string cacheKey, Order order)
        {
            var orderToCache = JsonSerializer.Serialize(order);

            await _cache.SetStringAsync(cacheKey, orderToCache, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
            });
        }

        private async Task<Tuple<string, string?>> GetCachedOrderById(Guid id)
        {
            string cacheKey = $"{CacheKeyPrefix}{id}";

            string? cachedItem = await _cache.GetStringAsync(cacheKey);

            return Tuple.Create(cacheKey, cachedItem);
        }
    }
}
