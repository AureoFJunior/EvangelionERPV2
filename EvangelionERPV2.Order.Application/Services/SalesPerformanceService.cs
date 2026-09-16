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
    /// <remarks>
    /// <para>
    /// Nothing here is stored except the target. What was sold, what was received and what
    /// commission that earns are all derived on read, because the alternative — a commission ledger
    /// — goes wrong the moment a boleto is settled, an order is refunded or a rate is corrected,
    /// and then two screens disagree about what a seller is owed.
    /// </para>
    /// <para>
    /// It lives in the Order module because <c>Order.UserId</c> is the only record of who sold what,
    /// and <see cref="ReceivableResolver"/> already lets this read the boleto side without a project
    /// reference into Bills.
    /// </para>
    /// </remarks>
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

            // Off unless switched on, and that default is a measurement rather than caution.
            //
            // Measured 2026-09-16 against the dev stack: the aggregate itself answers in 25ms, and a
            // CACHE HIT costs about 148ms - Redis is Upstash, roughly 25ms away, and a read is
            // several round trips. Caching a 25ms query behind a 148ms cache is a straight loss.
            //
            // It becomes the right trade on two conditions, both of which hold in production and
            // neither of which holds here: Redis in the same region, where a hit is sub-millisecond,
            // and a dataset where the aggregate is no longer cheap - this ran against ~5 000 orders,
            // not the millions a real tenant-year holds. Turn it on there, and check the same two
            // numbers before believing it helps.
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

            // The enterprise id is in the key, not merely in the value. A cache key that omitted it
            // would serve one tenant's commissions to another the moment two of them asked for the
            // same month - the entry is keyed by exactly the arguments that determine the answer.
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
                // A cache that is down must not take the report with it.
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

            // Not named `from`/`to`: those are query-expression keywords and the LINQ below is in
            // query syntax.
            var periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var periodEnd = periodStart.AddMonths(1);

            // One query instead of three loads.
            //
            // Aggregated in SQL, one row per seller, instead of reading the month into memory.
            //
            // The first rewrite of this method already stopped materialising whole entities and cut
            // the response from 1,681ms to 25ms - but it still called ToListAsync() on every order
            // in the month and grouped in C#, so memory stayed proportional to the month. That only
            // showed up once the data was realistic: at 5,081 orders it looked solved, and at
            // 200,001 orders twenty concurrent callers took the process to OutOfMemoryException.
            // Narrowing the row reduced the constant; it did not remove the growth.
            //
            // So the money rule is now expressed in the query. That is the thing this method spent
            // two revisions avoiding, because it gives "what has been received" a second definition
            // that can drift from ReceivableResolver. The guard is a test that runs the same matrix
            // of refund, payday and bill states through both and asserts they agree - if either
            // side changes alone, it fails. Keep that test.
            //
            // CreatedAt, not PaymentScheduledDate: a seller's month is when they closed the sale,
            // not when the customer agreed to pay for it.
            var settled = from order in _context.Set<Order>().AsNoTracking()
                          where order.IsActive == true
                                && order.EnterpriseId == enterpriseId
                                && order.CreatedAt >= periodStart
                                && order.CreatedAt < periodEnd
                                // ReceivableResolver.IsOrderRefunded, in the query. A refunded order
                                // contributes nothing to sold, received or the count.
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
                              // ReceivableResolver.ResolveReceivedAmount for a non-refunded order:
                              // no bill means the money arrived only if there is a payday; a bill
                              // means it arrived only if that bill is paid, and then the paid amount
                              // wins over the billed amount, which wins over the order total.
                              Received = bill == null
                                  ? (order.Payday != null ? order.TotalValue : 0m)
                                  : (bill.Status == BillStatus.Paid
                                      ? (bill.PaidAmount > 0m
                                          ? bill.PaidAmount.Value
                                          : (bill.Amount > 0m ? bill.Amount : order.TotalValue))
                                      : 0m)
                          };

            // Per seller. Rows come back one per user, not one per order.
            var perSeller = await settled
                .Where(row => row.UserId != null)
                .GroupBy(row => row.UserId!.Value)
                .Select(group => new SellerTotals(
                    group.Key,
                    group.Sum(row => row.TotalValue),
                    group.Sum(row => row.Received),
                    group.Count()))
                .ToListAsync(cancellation);

            // The company's own row, over every order in the month - including those with no user
            // behind them, which the queued path creates, and those belonging to service accounts.
            // Not the sum of the rows above, deliberately.
            var enterpriseTotals = await settled
                .GroupBy(_ => 1)
                .Select(group => new SellerTotals(
                    null,
                    group.Sum(row => row.TotalValue),
                    group.Sum(row => row.Received),
                    group.Count()))
                .FirstOrDefaultAsync(cancellation)
                ?? new SellerTotals(null, 0m, 0m, 0);

            // The sellers themselves: one small read of the tenant's users, not one row per order.
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

            // A service account is dropped, not shown under its user id. Its orders still count in the
            // company's row below, which is built from every order rather than from the sum of
            // these - the money was real, the seller was not a person.
            //
            // Dropped on the *user*, not on the order: an order with a UserId nobody can resolve is
            // a different problem (a deleted user) and still deserves a row, which is why an
            // unresolved id falls back to the id itself as the name.
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

            // Not IsActive: a target set for somebody who has since left still explains a past
            // month. A service account never had a target worth explaining.
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
                // A target for somebody else's employee would be invisible to them and would still
                // occupy this tenant's unique index for that competência.
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
                // Null rather than zero without a target: "no target set" and "missed the target
                // entirely" are different things and the screen colours them differently.
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

        // BRL convention, half away from zero — Math.Round defaults to banker's and would disagree
        // with the enterprise balance and the payroll figures.
        private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
