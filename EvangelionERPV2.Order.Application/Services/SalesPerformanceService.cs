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
            IRepository<Order> orderRepository,
            IRepository<Bill> billRepository,
            IRepository<User> userRepository,
            IRepository<SalesGoal> salesGoalRepository)
        {
            _context = context;
            _cache = cache;
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

            var cached = await ReadCacheAsync(cacheKey);
            if (cached != null)
                return cached;

            var performance = await BuildPerformanceAsync(enterpriseId, year, month, cancellation);

            await WriteCacheAsync(cacheKey, performance, IsClosedMonth(year, month) ? ClosedMonthCacheTtl : CurrentMonthCacheTtl);

            return performance;
        }

        private static bool IsClosedMonth(int year, int month)
        {
            var now = DateTime.UtcNow;
            return year < now.Year || (year == now.Year && month < now.Month);
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
            // This used to read the whole month of Order entities, then every Bill whose OrderId was
            // in that set (an IN clause thousands of ids long), then every User in the tenant - and
            // aggregate all of it in C#. Each concurrent caller paid for its own full copy, which is
            // why the process reached 13 GiB and 851% CPU under load while answering in over 30
            // seconds.
            //
            // Now the join and the "latest live bill per order" pick happen in SQL, and only the
            // columns the money rules actually read come back. The rules themselves stay in
            // ReceivableResolver - restating them in SQL would give money two definitions that can
            // drift apart.
            //
            // CreatedAt, not PaymentScheduledDate: a seller's month is when they closed the sale,
            // not when the customer agreed to pay for it.
            var rows = await (
                from order in _context.Set<Order>().AsNoTracking()
                where order.IsActive == true
                      && order.EnterpriseId == enterpriseId
                      && order.CreatedAt >= periodStart
                      && order.CreatedAt < periodEnd
                join user in _context.Set<User>().AsNoTracking()
                    on order.UserId equals user.Id into userJoin
                from user in userJoin.DefaultIfEmpty()
                    // Bill carries no EnterpriseId - it is tenant-scoped through Order, so it is
                    // joined by order rather than by tenant. Newest non-cancelled bill wins, which
                    // is what SelectLiveBillsByOrder did in memory.
                let bill = _context.Set<Bill>().AsNoTracking()
                    .Where(candidate => candidate.IsActive == true
                                        && candidate.OrderId == order.Id
                                        && candidate.Status != BillStatus.Cancelled)
                    .OrderByDescending(candidate => candidate.CreatedAt)
                    .FirstOrDefault()
                select new PerformanceRow(
                    order.UserId,
                    order.TotalValue,
                    order.Status,
                    order.RefundedAt,
                    order.Payday.HasValue,
                    user != null,
                    user != null && user.IsActive == true,
                    user != null && user.IsServiceAccount,
                    user != null ? user.FirstName : null,
                    user != null ? user.LastName : null,
                    user != null ? user.UserName : null,
                    user != null ? user.CommissionRate : null,
                    bill != null ? bill.Status : (BillStatus?)null,
                    bill != null ? bill.PaidAmount : null,
                    bill != null ? bill.Amount : (decimal?)null))
                .ToListAsync(cancellation);

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
            // these — the money was real, the seller was not a person.
            //
            // Dropped on the *user*, not on the order: an order with a UserId nobody can resolve is
            // a different problem (a deleted user) and still deserves a row, which is what the id
            // fallback in ResolveSellerName is for.
            var sellerRows = rows
                .Where(row => row.UserId.HasValue && !row.IsServiceAccount)
                .GroupBy(row => row.UserId!.Value)
                .Select(group =>
                {
                    // A seller is "known" only when the user exists, is active and is a person -
                    // exactly the set the old sellers dictionary held. Anything else falls back to
                    // the id, which is what an order left behind by a deleted user looked like
                    // before and still does.
                    var first = group.First();
                    var known = first.UserFound && first.SellerIsActive && !first.IsServiceAccount;

                    return BuildRow(
                        group.Key,
                        known ? ResolveSellerName(first) : group.Key.ToString(),
                        group,
                        goalByUser.TryGetValue(group.Key, out var target) ? target : null,
                        known ? first.CommissionRate : null);
                })
                .OrderByDescending(row => row.SoldAmount)
                .ThenBy(row => row.SellerName)
                .ToList();

            // The company's own row, not the sum of the rows above: an order created through the
            // queued path has no user behind it and belongs to the company's number alone.
            var enterpriseRow = BuildRow(null, string.Empty, rows, enterpriseGoal, null);

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
        /// One row from already-filtered projection rows. Same arithmetic as before; it simply no
        /// longer needs whole entities or a dictionary of bills to do it, because the live bill
        /// travelled with each row out of SQL.
        /// </summary>
        private static SalesPerformanceRowDTO BuildRow(
            Guid? userId,
            string sellerName,
            IEnumerable<PerformanceRow> rows,
            decimal? target,
            decimal? commissionRate)
        {
            var orderList = rows
                .Where(row => !ReceivableResolver.IsOrderRefunded(row.RefundedAt, row.Status))
                .ToList();

            var sold = orderList.Sum(row => row.TotalValue);
            var received = orderList.Sum(row => ReceivableResolver.ResolveReceivedAmount(
                row.TotalValue,
                ReceivableResolver.IsOrderRefunded(row.RefundedAt, row.Status),
                row.HasPayday,
                row.BillStatus,
                row.BillPaidAmount,
                row.BillAmount));

            return new SalesPerformanceRowDTO
            {
                UserId = userId,
                SellerName = sellerName,
                SoldAmount = Round(sold),
                ReceivedAmount = Round(received),
                OrderCount = orderList.Count,
                TargetAmount = target,
                // Null rather than zero without a target: "no target set" and "missed the target
                // entirely" are different things and the screen colours them differently.
                AttainmentPercent = target is > 0 ? Round(sold / target.Value * 100m) : null,
                CommissionRate = commissionRate,
                CommissionAmount = commissionRate.HasValue ? Round(received * commissionRate.Value) : null
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

        private static string ResolveSellerName(PerformanceRow row)
        {
            var name = $"{row.FirstName} {row.LastName}".Trim();
            return string.IsNullOrWhiteSpace(name) ? (row.UserName ?? string.Empty) : name;
        }

        /// <summary>
        /// Only the columns the monthly aggregate reads. Deliberately not an entity: the point of
        /// this type is that a month of sales costs a few fields per order instead of two full
        /// object graphs.
        /// </summary>
        private sealed record PerformanceRow(
            Guid? UserId,
            decimal TotalValue,
            int Status,
            DateTime? RefundedAt,
            bool HasPayday,
            bool UserFound,
            bool SellerIsActive,
            bool IsServiceAccount,
            string? FirstName,
            string? LastName,
            string? UserName,
            decimal? CommissionRate,
            BillStatus? BillStatus,
            decimal? BillPaidAmount,
            decimal? BillAmount);

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
