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

namespace EvangelionERPV2.OrderModule.Domain.Repositories
{
    public class OrderRepository : Repository<Order>, IOrderRepository<Order>
    {
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
                var orderFromCache = JsonSerializer.Deserialize<Order>(cachedItem);
                if (orderFromCache != null)
                    return orderFromCache;
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

        public async override Task<IEnumerable<Order>> GetAllAsync(Func<Order, bool>? predicate)
        {
            IQueryable<Order> query = _context.Set<Order>()
                .Include(o => o.Customer)
                .Include(o => o.OrderedProduct!)
                    .ThenInclude(op => op.Product)
                .Where(o => o.IsActive ?? false)
                .AsSplitQuery()
                .AsNoTracking();
            IEnumerable<Order> result = predicate != null
                ? query.AsEnumerable().Where(predicate).ToList()
                : await query.ToListAsync();

            if (result?.Any() == true)
                return result;

            throw new NotFoundDatabaseException();
        }

        public async override Task<IEnumerable<Order>> GetAllAsync(int? pageNumber, int? pageSize, Func<Order, bool>? predicate = null)
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
            IEnumerable<Order> result;
            int skip = (pageNumber.Value - 1) * pageSize.Value;

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
                result = await query.Skip(skip).Take(pageSize.Value).ToListAsync();
            }

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
            string cacheKey = $"Order:{id}";

            string? cachedItem = await _cache.GetStringAsync(cacheKey);

            return Tuple.Create(cacheKey, cachedItem);
        }
    }
}
