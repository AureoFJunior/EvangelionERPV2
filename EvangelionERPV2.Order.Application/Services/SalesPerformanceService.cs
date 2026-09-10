using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
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
        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<Bill> _billRepository;
        private readonly IRepository<User> _userRepository;
        private readonly IRepository<SalesGoal> _salesGoalRepository;

        public SalesPerformanceService(
            IRepository<Order> orderRepository,
            IRepository<Bill> billRepository,
            IRepository<User> userRepository,
            IRepository<SalesGoal> salesGoalRepository)
        {
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

            var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = from.AddMonths(1);

            // CreatedAt, not PaymentScheduledDate: a seller's month is when they closed the sale,
            // not when the customer agreed to pay for it.
            var orders = (await _orderRepository.GetAllAsync(order =>
                    order.IsActive == true
                    && order.EnterpriseId == enterpriseId
                    && order.CreatedAt >= from
                    && order.CreatedAt < to))
                .ToList();

            var orderIds = orders.Select(order => order.Id).ToList();

            // Bill carries no EnterpriseId — it is tenant-scoped through Order, which is why this
            // is keyed by the order ids already filtered above rather than by the tenant.
            var bills = orderIds.Count == 0
                ? new List<Bill>()
                : (await _billRepository.GetAllAsync(bill => bill.IsActive == true && orderIds.Contains(bill.OrderId))).ToList();

            var liveBills = ReceivableResolver.SelectLiveBillsByOrder(bills);

            // One read, split two ways: the same rows answer "who is this seller" and "which of these
            // are not people at all", and asking the tenant's users twice buys nothing.
            var users = (await _userRepository.GetAllAsync(user => user.EnterpriseId == enterpriseId)).ToList();

            var sellers = users
                .Where(user => user.IsActive == true && !user.IsServiceAccount)
                .ToDictionary(user => user.Id, user => user);

            var serviceAccountIds = users
                .Where(user => user.IsServiceAccount)
                .Select(user => user.Id)
                .ToHashSet();

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
            var rows = orders
                .Where(order => order.UserId.HasValue)
                .GroupBy(order => order.UserId!.Value)
                .Where(group => !serviceAccountIds.Contains(group.Key))
                .Select(group =>
                {
                    sellers.TryGetValue(group.Key, out var seller);
                    var rate = seller?.CommissionRate;

                    return BuildRow(
                        group.Key,
                        ResolveSellerName(seller, group.Key),
                        group,
                        liveBills,
                        goalByUser.TryGetValue(group.Key, out var target) ? target : null,
                        rate);
                })
                .OrderByDescending(row => row.SoldAmount)
                .ThenBy(row => row.SellerName)
                .ToList();

            // The company's own row, not the sum of the rows above: an order created through the
            // queued path has no user behind it and belongs to the company's number alone.
            var enterpriseRow = BuildRow(null, string.Empty, orders, liveBills, enterpriseGoal, null);

            return new SalesPerformanceDTO
            {
                Year = year,
                Month = month,
                Sellers = rows,
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

        private static SalesPerformanceRowDTO BuildRow(
            Guid? userId,
            string sellerName,
            IEnumerable<Order> orders,
            IReadOnlyDictionary<Guid, Bill> liveBills,
            decimal? target,
            decimal? commissionRate)
        {
            var orderList = orders.Where(order => !ReceivableResolver.IsOrderRefunded(order)).ToList();

            var sold = orderList.Sum(order => order.TotalValue);
            var received = orderList.Sum(order =>
                ReceivableResolver.ResolveReceivedAmount(order, liveBills.TryGetValue(order.Id, out var bill) ? bill : null));

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

        private static string ResolveSellerName(User? seller, Guid userId)
        {
            if (seller == null)
                return userId.ToString();

            var name = $"{seller.FirstName} {seller.LastName}".Trim();
            return string.IsNullOrWhiteSpace(name) ? seller.UserName : name;
        }

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
