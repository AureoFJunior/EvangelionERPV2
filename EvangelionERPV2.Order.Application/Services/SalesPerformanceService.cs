using Serilog;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.OrderModule.Application.Services
{
    /// <summary>
    /// Metas e comissão: one competência of selling, read from the orders that already exist.
    /// </summary>
    public class SalesPerformanceService : ISalesPerformanceService
    {
        /// <summary>
        /// Read side for the monthly aggregate. The repositories below can only hand back whole
        /// entities, and this endpoint used to materialise a month of Order plus every matching
        /// Bill plus every User just to add up a few columns - which is what put 13 GiB on the
        /// process under load and made it the slowest path in the API at 1.68s p95.
        /// </summary>
        private readonly AppDbContext _context;
        private readonly IDistributedCache _cache;
        private readonly bool _cacheEnabled;

        private const string CacheKeyPrefix = "SalesPerformance:";
        private static readonly JsonSerializerOptions CacheSerializerOptions = new(JsonSerializerDefaults.Web);

        /// <summary>
        /// A closed month cannot change - its orders are in the past - so it is cached for long
        /// enough to be free on every later read. The month in progress changes with every sale, so
        /// it gets a short window that still absorbs a dashboard being refreshed by a room full of
        /// people without serving anything visibly stale.
        /// </summary>
        private static readonly TimeSpan ClosedMonthCacheTtl = TimeSpan.FromHours(12);
        private static readonly TimeSpan CurrentMonthCacheTtl = TimeSpan.FromSeconds(30);

        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<Bill> _billRepository;
        private readonly IRepository<User> _userRepository;
        private readonly IRepository<SalesGoal> _salesGoalRepository;

        public SalesPerformanceService(
            AppDbContext context,
            IDistributedCache cache,
            IConfiguration configuration,
            IRepository<Order> orderRepository,
            IRepository<Bill> billRepository,
            IRepository<User> userRepository,
            IRepository<SalesGoal> salesGoalRepository)
        {
            _context = context;
            _cache = cache;

            _cacheEnabled = configuration.GetValue<bool?>("SalesPerformance:CacheEnabled") ?? false;
            _orderRepository = orderRepository;
            _billRepository = billRepository;
            _userRepository = userRepository;
            _salesGoalRepository = salesGoalRepository;
        }

        /// <inheritdoc />
        /// <remarks>
        /// ponytail: computes the whole month and then takes one row. It is one pass over one
        /// enterprise-month, and sharing the calculation is what guarantees the seller's own screen
        /// and their manager's agree to the cent. Give it its own query if a tenant's month gets
        /// big enough to feel it.
        /// </remarks>
        public async Task<SalesPerformanceRowDTO?> GetSellerPerformanceAsync(
            Guid enterpriseId,
            Guid userId,
            int year,
            int month,
            CancellationToken cancellation = default)
        {
            if (userId == Guid.Empty)
                return null;

            var performance = await GetPerformanceAsync(enterpriseId, year, month, cancellation);

            return performance.Sellers.FirstOrDefault(row => row.UserId == userId);
        }

        public async Task<SalesPerformanceDTO> GetPerformanceAsync(Guid enterpriseId, int year, int month, CancellationToken cancellation = default)
        {
            AssertCompetencia(year, month);

            var cacheKey = $"{CacheKeyPrefix}{enterpriseId}:{year}:{month:D2}";

            if (_cacheEnabled)
            {
                var cached = await ReadCacheAsync(cacheKey);
                if (cached != null)
                    return await RehydrateSellerNamesAsync(cached, enterpriseId, cancellation);
            }

            var performance = await BuildPerformanceAsync(enterpriseId, year, month, cancellation);

            if (_cacheEnabled)
                await WriteCacheAsync(cacheKey, Pseudonymise(performance), IsClosedMonth(year, month) ? ClosedMonthCacheTtl : CurrentMonthCacheTtl);

            return performance;
        }

        private static bool IsClosedMonth(int year, int month)
        {
            var now = DateTime.UtcNow;
            return year < now.Year || (year == now.Year && month < now.Month);
        }

        /// <summary>
        /// Strips seller names before anything is written to the cache.
        /// <para>
        /// The cache is Redis, and in this deployment Redis is a third-party service outside the
        /// application's own trust boundary. A seller's name is personal data; their commission is
        /// pay. Keyed by user id alone, a cache entry that escaped says "this id sold this much" -
        /// pseudonymous, and worth far less to whoever reads it. The names never leave the process.
        /// </para>
        /// <para>
        /// The row still carries UserId, which is what <see cref="RehydrateSellerNamesAsync"/> uses
        /// to put the names back from the database on the way out - a handful of rows, unlike the
        /// month of orders the cache exists to avoid recomputing.
        /// </para>
        /// </summary>
        private static SalesPerformanceDTO Pseudonymise(SalesPerformanceDTO performance)
        {
            return new SalesPerformanceDTO
            {
                Year = performance.Year,
                Month = performance.Month,
                Enterprise = performance.Enterprise,
                Sellers = performance.Sellers
                    .Select(row => new SalesPerformanceRowDTO
                    {
                        UserId = row.UserId,
                        SellerName = string.Empty,
                        SoldAmount = row.SoldAmount,
                        ReceivedAmount = row.ReceivedAmount,
                        OrderCount = row.OrderCount,
                        TargetAmount = row.TargetAmount,
                        AttainmentPercent = row.AttainmentPercent,
                        CommissionRate = row.CommissionRate,
                        CommissionAmount = row.CommissionAmount
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// Puts the seller names back on a cached result. One small query keyed by the ids already
        /// in the payload; a row whose user has since been removed keeps the id as its name, which
        /// is what the uncached path does too.
        /// </summary>
        private async Task<SalesPerformanceDTO> RehydrateSellerNamesAsync(
            SalesPerformanceDTO cached,
            Guid enterpriseId,
            CancellationToken cancellation)
        {
            var ids = cached.Sellers.Where(row => row.UserId.HasValue).Select(row => row.UserId!.Value).ToList();
            if (ids.Count == 0)
                return cached;

            var names = await _context.Set<User>().AsNoTracking()
                .Where(user => user.EnterpriseId == enterpriseId
                               && user.IsActive == true
                               && !user.IsServiceAccount
                               && ids.Contains(user.Id))
                .Select(user => new { user.Id, user.FirstName, user.LastName, user.UserName })
                .ToDictionaryAsync(user => user.Id, cancellation);

            foreach (var row in cached.Sellers)
            {
                if (row.UserId.HasValue && names.TryGetValue(row.UserId.Value, out var user))
                {
                    var name = $"{user.FirstName} {user.LastName}".Trim();
                    row.SellerName = string.IsNullOrWhiteSpace(name) ? user.UserName : name;
                }
                else
                {
                    row.SellerName = row.UserId?.ToString() ?? string.Empty;
                }
            }

            return cached;
        }

        private async Task<SalesPerformanceDTO?> ReadCacheAsync(string key)
        {
            try
            {
                var cachedValue = await _cache.GetStringAsync(key);
                if (string.IsNullOrWhiteSpace(cachedValue))
                    return null;

                return JsonSerializer.Deserialize<SalesPerformanceDTO>(cachedValue, CacheSerializerOptions);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("Sales performance cache read failed. ErrorType={ErrorType}", ex.GetType().Name);
                return null;
            }
        }

        private async Task WriteCacheAsync(string key, SalesPerformanceDTO performance, TimeSpan ttl)
        {
            try
            {
                await _cache.SetStringAsync(
                    key,
                    JsonSerializer.Serialize(performance, CacheSerializerOptions),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("Sales performance cache write failed. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }

        private async Task<SalesPerformanceDTO> BuildPerformanceAsync(Guid enterpriseId, int year, int month, CancellationToken cancellation)
        {
            var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var periodEnd = periodStart.AddMonths(1);

            var settled = from order in _context.Set<Order>().AsNoTracking()
                          where order.IsActive == true
                                && order.EnterpriseId == enterpriseId
                                && order.CreatedAt >= periodStart
                                && order.CreatedAt < periodEnd
                                && order.RefundedAt == null
                                && order.Status != (int)EnumOrderStatus.Refund
                          let bill = _context.Set<Bill>().AsNoTracking()
                              .Where(candidate => candidate.IsActive == true
                                                  && candidate.OrderId == order.Id
                                                  && candidate.Status != BillStatus.Cancelled)
                              .OrderByDescending(candidate => candidate.CreatedAt)
                              .FirstOrDefault()
                          select new
                          {
                              order.UserId,
                              order.TotalValue,
                              Received = bill == null
                                  ? (order.Payday != null ? order.TotalValue : 0m)
                                  : (bill.Status == BillStatus.Paid
                                      ? (bill.PaidAmount > 0m
                                          ? bill.PaidAmount.Value
                                          : (bill.Amount > 0m ? bill.Amount : order.TotalValue))
                                      : 0m)
                          };

            var perSeller = await settled
                .Where(row => row.UserId != null)
                .GroupBy(row => row.UserId!.Value)
                .Select(group => new SellerTotals(
                    group.Key,
                    group.Sum(row => row.TotalValue),
                    group.Sum(row => row.Received),
                    group.Count()))
                .ToListAsync(cancellation);

            var enterpriseTotals = await settled
                .GroupBy(_ => 1)
                .Select(group => new SellerTotals(
                    null,
                    group.Sum(row => row.TotalValue),
                    group.Sum(row => row.Received),
                    group.Count()))
                .FirstOrDefaultAsync(cancellation)
                ?? new SellerTotals(null, 0m, 0m, 0);

            var users = await _context.Set<User>().AsNoTracking()
                .Where(user => user.EnterpriseId == enterpriseId)
                .Select(user => new SellerIdentity(
                    user.Id,
                    user.FirstName,
                    user.LastName,
                    user.UserName,
                    user.CommissionRate,
                    user.IsActive == true,
                    user.IsServiceAccount))
                .ToDictionaryAsync(user => user.Id, cancellation);

            var goals = (await _salesGoalRepository.GetAllAsync(goal =>
                    goal.IsActive == true
                    && goal.EnterpriseId == enterpriseId
                    && goal.Year == year
                    && goal.Month == month))
                .ToList();

            var goalByUser = goals
                .Where(goal => goal.UserId.HasValue)
                .ToDictionary(goal => goal.UserId!.Value, goal => goal.TargetAmount);
            var enterpriseGoal = goals.FirstOrDefault(goal => !goal.UserId.HasValue)?.TargetAmount;

            var sellerRows = perSeller
                .Where(totals => !(users.TryGetValue(totals.UserId!.Value, out var account) && account.IsServiceAccount))
                .Select(totals =>
                {
                    var found = users.TryGetValue(totals.UserId!.Value, out var seller);
                    var known = found && seller!.IsActive && !seller.IsServiceAccount;

                    return BuildRow(
                        totals.UserId,
                        known ? ResolveSellerName(seller!) : totals.UserId!.Value.ToString(),
                        totals,
                        goalByUser.TryGetValue(totals.UserId!.Value, out var target) ? target : null,
                        known ? seller!.CommissionRate : null);
                })
                .OrderByDescending(row => row.SoldAmount)
                .ThenBy(row => row.SellerName)
                .ToList();

            var enterpriseRow = BuildRow(null, string.Empty, enterpriseTotals, enterpriseGoal, null);

            return new SalesPerformanceDTO
            {
                Year = year,
                Month = month,
                Sellers = sellerRows,
                Enterprise = enterpriseRow
            };
        }

        public async Task<IReadOnlyList<SalesGoalDTO>> GetGoalsAsync(Guid enterpriseId, int year, CancellationToken cancellation = default)
        {
            if (year < 2000 || year > 2100)
                throw new InsertDatabaseException("Year must be between 2000 and 2100.");

            var goals = (await _salesGoalRepository.GetAllAsync(goal =>
                    goal.IsActive == true && goal.EnterpriseId == enterpriseId && goal.Year == year))
                .ToList();

            var sellers = (await _userRepository.GetAllAsync(user =>
                    user.EnterpriseId == enterpriseId && user.IsServiceAccount == false))
                .ToDictionary(user => user.Id, user => user);

            return goals
                .Where(goal => !goal.UserId.HasValue || sellers.ContainsKey(goal.UserId.Value))
                .Select(goal => new SalesGoalDTO
                {
                    Id = goal.Id,
                    UserId = goal.UserId,
                    SellerName = goal.UserId.HasValue && sellers.TryGetValue(goal.UserId.Value, out var seller)
                        ? ResolveSellerName(seller, goal.UserId.Value)
                        : null,
                    Year = goal.Year,
                    Month = goal.Month,
                    TargetAmount = goal.TargetAmount
                })
                .OrderBy(goal => goal.Month)
                .ThenBy(goal => goal.SellerName)
                .ToList();
        }

        public async Task<SalesGoal> UpsertGoalAsync(Guid enterpriseId, UpsertSalesGoalRequestDTO request, CancellationToken cancellation = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            AssertCompetencia(request.Year, request.Month);

            if (request.TargetAmount < 0)
                throw new InsertDatabaseException("Target amount cannot be negative.");

            if (request.UserId.HasValue)
            {
                var seller = (await _userRepository.GetAllAsync(user =>
                        user.Id == request.UserId.Value
                        && user.EnterpriseId == enterpriseId
                        && user.IsActive == true
                        && user.IsServiceAccount == false))
                    .FirstOrDefault();

                if (seller == null)
                    throw new NotFoundDatabaseException("Seller was not found in this enterprise.");
            }

            var existing = (await _salesGoalRepository.GetAllAsync(goal =>
                    goal.IsActive == true
                    && goal.EnterpriseId == enterpriseId
                    && goal.UserId == request.UserId
                    && goal.Year == request.Year
                    && goal.Month == request.Month))
                .FirstOrDefault();

            if (existing != null)
            {
                existing.TargetAmount = request.TargetAmount;
                existing.UpdatedAt = DateTime.UtcNow;
                var updated = _salesGoalRepository.Update(existing);
                await _salesGoalRepository.CommitAsync(cancellation);
                return updated;
            }

            var goal = new SalesGoal
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                UserId = request.UserId,
                Year = request.Year,
                Month = request.Month,
                TargetAmount = request.TargetAmount,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            var created = await _salesGoalRepository.CreateAsync(goal);
            await _salesGoalRepository.CommitAsync(cancellation);
            return created;
        }

        /// <summary>
        /// Builds one row from totals the database already added up. The arithmetic left here is
        /// only what depends on the seller rather than on the orders - attainment against a target,
        /// and commission on what was received.
        /// </summary>
        private static SalesPerformanceRowDTO BuildRow(
            Guid? userId,
            string sellerName,
            SellerTotals totals,
            decimal? target,
            decimal? commissionRate)
        {
            return new SalesPerformanceRowDTO
            {
                UserId = userId,
                SellerName = sellerName,
                SoldAmount = Round(totals.Sold),
                ReceivedAmount = Round(totals.Received),
                OrderCount = totals.OrderCount,
                TargetAmount = target,
                AttainmentPercent = target is > 0 ? Round(totals.Sold / target.Value * 100m) : null,
                CommissionRate = commissionRate,
                CommissionAmount = commissionRate.HasValue ? Round(totals.Received * commissionRate.Value) : null
            };
        }

        /// <summary>
        /// Entity form, still used by the goals listing, which reads whole users by design - it
        /// returns at most one row per seller rather than one per order.
        /// </summary>
        private static string ResolveSellerName(User? seller, Guid userId)
        {
            if (seller == null)
                return userId.ToString();

            var name = $"{seller.FirstName} {seller.LastName}".Trim();
            return string.IsNullOrWhiteSpace(name) ? seller.UserName : name;
        }

        private static string ResolveSellerName(SellerIdentity seller)
        {
            var name = $"{seller.FirstName} {seller.LastName}".Trim();
            return string.IsNullOrWhiteSpace(name) ? (seller.UserName ?? string.Empty) : name;
        }

        /// <summary>
        /// One seller's month, already summed by the database. The whole point of this type is that
        /// the service holds one of these per seller rather than one row per order.
        /// </summary>
        private sealed record SellerTotals(Guid? UserId, decimal Sold, decimal Received, int OrderCount);

        /// <summary>
        /// Who a seller is. Read once for the tenant, not once per order.
        /// </summary>
        private sealed record SellerIdentity(
            Guid Id,
            string? FirstName,
            string? LastName,
            string? UserName,
            decimal? CommissionRate,
            bool IsActive,
            bool IsServiceAccount);

        private static void AssertCompetencia(int year, int month)
        {
            if (year < 2000 || year > 2100)
                throw new InsertDatabaseException("Year must be between 2000 and 2100.");

            if (month < 1 || month > 12)
                throw new InsertDatabaseException("Month must be between 1 and 12.");
        }

        private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
