using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// The ficha do cliente: who this customer is, what they have bought, and what they still owe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It lives in the Bills module for the same reason <see cref="DailyPanelService"/> does: most
    /// of the answer is money, and the aging rule already lives here in
    /// <see cref="ReceivablesReportService"/>. Putting it in the Customer module would mean either a
    /// project reference into Bills or a second copy of the aging buckets, and a second opinion
    /// about what a customer owes is exactly what <see cref="ReceivableResolver"/> exists to prevent.
    /// </para>
    /// <para>
    /// Nothing here is stored. Every figure is derived on read, so the ficha cannot drift from the
    /// carteira the way a cached customer balance would.
    /// </para>
    /// </remarks>
    public class CustomerSummaryService : ICustomerSummaryService
    {
        private const int RecentOrderCount = 10;
        private const int TopProductCount = 5;

        private readonly IRepository<Customer> _customerRepository;
        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<Bill> _billRepository;
        private readonly IRepository<OrderedProduct> _orderedProductRepository;
        private readonly IRepository<Product> _productRepository;
        private readonly IReceivablesReportService _receivablesReportService;
        private readonly AppDbContext _context;

        public CustomerSummaryService(
            IRepository<Customer> customerRepository,
            IRepository<Order> orderRepository,
            IRepository<Bill> billRepository,
            IRepository<OrderedProduct> orderedProductRepository,
            IRepository<Product> productRepository,
            IReceivablesReportService receivablesReportService,
            AppDbContext context)
        {
            _customerRepository = customerRepository;
            _orderRepository = orderRepository;
            _billRepository = billRepository;
            _orderedProductRepository = orderedProductRepository;
            _productRepository = productRepository;
            _receivablesReportService = receivablesReportService;
            _context = context;
        }

        public async Task<CustomerSummaryDTO?> GetSummaryAsync(
            Guid customerId,
            Guid enterpriseId,
            bool includeFinancial,
            CancellationToken cancellation = default)
        {
            // GetPagedAsync, never GetAllAsync: CustomerRepository throws NotFoundDatabaseException
            // on an empty result and that maps to 204, so a miss would answer "no content" from
            // deep inside instead of letting the caller decide.
            var (customers, _) = await _customerRepository.GetPagedAsync(
                customer => customer.Id == customerId && customer.EnterpriseId == enterpriseId && customer.IsActive == true,
                pageNumber: 1,
                pageSize: 1);

            var customer = customers.FirstOrDefault();
            if (customer == null)
                return null;

            // Every figure below is an aggregate over the customer's whole history, so it is
            // computed in SQL. Loading the orders to add them up here is what made this endpoint
            // answer OutOfMemoryException for a customer with 1.8M of them.
            var customerOrders = _context.Set<Order>().AsNoTracking()
                .Where(order => order.IsActive == true
                                && order.EnterpriseId == enterpriseId
                                && order.CustomerId == customerId);

            // Refunds are excluded from what the customer "bought": the money went back.
            // ReceivableResolver.IsOrderRefunded, in the query.
            var realOrders = customerOrders
                .Where(order => order.RefundedAt == null && order.Status != (int)EnumOrderStatus.Refund);

            var stats = await realOrders
                .GroupBy(order => 1)
                .Select(group => new
                {
                    Count = group.Count(),
                    Total = group.Sum(order => order.TotalValue),
                    First = group.Min(order => order.CreatedAt),
                    Last = group.Max(order => order.CreatedAt)
                })
                .FirstOrDefaultAsync(cancellation);

            var orderCount = stats?.Count ?? 0;
            var totalPurchased = Round(stats?.Total ?? 0m);

            var summary = new CustomerSummaryDTO
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                Email = customer.Email,
                PhoneNumber = customer.PhoneNumber,
                Document = customer.Document,
                Municipio = customer.Municipio,
                Uf = customer.Uf,
                OrderCount = orderCount,
                TotalPurchased = totalPurchased,
                AverageTicket = orderCount > 0 ? Round(totalPurchased / orderCount) : null,
                FirstPurchaseAtUtc = orderCount > 0 ? stats!.First : null,
                LastPurchaseAtUtc = orderCount > 0 ? stats!.Last : null,
                RecentOrders = await ReadRecentOrdersAsync(customerOrders, cancellation),
                TopProducts = await BuildTopProductsAsync(realOrders, enterpriseId, cancellation)
            };

            if (summary.LastPurchaseAtUtc.HasValue)
                summary.DaysSinceLastPurchase = (DateTime.UtcNow.Date - summary.LastPurchaseAtUtc.Value.Date).Days;

            if (includeFinancial)
                summary.Financial = await BuildFinancialAsync(customer, realOrders, enterpriseId, cancellation);

            return summary;
        }

        private async Task<CustomerFinancialSummaryDTO> BuildFinancialAsync(
            Customer customer,
            IQueryable<Order> realOrders,
            Guid enterpriseId,
            CancellationToken cancellation)
        {
            // ReceivableResolver.ResolveOpenExposure, in the query. Refunded orders are already out
            // of realOrders, so what is left is: a settled title owes nothing, and anything else
            // owes the billed amount where there is one and the order total where there is not.
            // CustomerSummaryExposureAgreementTests runs both this and the resolver over the same
            // cases and asserts they agree — keep that test, it is the only thing holding the two
            // definitions of exposure together.
            var exposure = Round(await (
                from order in realOrders
                let bill = _context.Set<Bill>().AsNoTracking()
                    .Where(candidate => candidate.IsActive == true
                                        && candidate.OrderId == order.Id
                                        && candidate.Status != BillStatus.Cancelled)
                    .OrderByDescending(candidate => candidate.CreatedAt)
                    .FirstOrDefault()
                select bill == null
                    ? order.TotalValue
                    : bill.Status == BillStatus.Paid
                        ? 0m
                        : (bill.Amount > 0m ? bill.Amount : order.TotalValue))
                .SumAsync(cancellation));

            var financial = new CustomerFinancialSummaryDTO
            {
                OpenExposure = exposure,
                CreditLimit = customer.CreditLimit,
                // Null, not zero, when no limit is set: an unlimited customer has no "available"
                // figure, and showing zero would read as "blocked".
                CreditAvailable = customer.CreditLimit.HasValue ? Round(customer.CreditLimit.Value - exposure) : null
            };

            // The aging rule is reused rather than repeated — one opinion about which bucket a
            // receivable falls in. The customer-scoped overload, because the tenant-wide one builds
            // a position object per open order to read this single row: 1.1 GiB for a customer with
            // 1.8M of them. That was the ponytail note this used to carry, and a big tenant felt it.
            var row = await _receivablesReportService.GetCustomerAgingAsync(enterpriseId, customer.Id, cancellation);

            financial.NotYetDue = row.NotYetDue;
            financial.Days1To30 = row.Days1To30;
            financial.Days31To60 = row.Days31To60;
            financial.Days61To90 = row.Days61To90;
            financial.Days90Plus = row.Days90Plus;
            financial.OverdueAmount = Round(row.Days1To30 + row.Days31To60 + row.Days61To90 + row.Days90Plus);

            return financial;
        }

        /// <summary>
        /// The last <see cref="RecentOrderCount"/> orders, refunds included — the ficha shows what
        /// happened, not only what counted.
        /// </summary>
        private async Task<IReadOnlyList<CustomerOrderSummaryDTO>> ReadRecentOrdersAsync(
            IQueryable<Order> customerOrders,
            CancellationToken cancellation)
        {
            return await (
                from order in customerOrders
                orderby order.CreatedAt descending
                // The live title: newest not-cancelled, the same rule
                // ReceivableResolver.SelectLiveBillsByOrder applies in memory. Written out rather
                // than called through a helper because EF does not inline a method into a query -
                // it evaluates it client-side, where it throws.
                let bill = _context.Set<Bill>().AsNoTracking()
                    .Where(candidate => candidate.IsActive == true
                                        && candidate.OrderId == order.Id
                                        && candidate.Status != BillStatus.Cancelled)
                    .OrderByDescending(candidate => candidate.CreatedAt)
                    .FirstOrDefault()
                select new CustomerOrderSummaryDTO
                {
                    OrderId = order.Id,
                    CreatedAtUtc = order.CreatedAt,
                    TotalValue = order.TotalValue,
                    Status = order.Status,
                    IsRefunded = order.RefundedAt != null || order.Status == (int)EnumOrderStatus.Refund,
                    BillStatus = bill == null ? null : (int)bill.Status,
                    BillDueDate = bill == null ? null : bill.DueDate
                })
                .Take(RecentOrderCount)
                .ToListAsync(cancellation);
        }

        private async Task<IReadOnlyList<CustomerTopProductDTO>> BuildTopProductsAsync(
            IQueryable<Order> realOrders,
            Guid enterpriseId,
            CancellationToken cancellation)
        {
            // Ranked in SQL. Reading every line of every order to rank five products is the same
            // defect as the one above, one join further out.
            var ranked = await (
                from line in _context.Set<OrderedProduct>().AsNoTracking()
                join order in realOrders on line.OrderId equals order.Id
                where line.IsActive == true && line.Quantity > 0
                group line by line.ProductId into grouped
                select new CustomerTopProductDTO
                {
                    ProductId = grouped.Key,
                    Quantity = grouped.Sum(line => line.Quantity),
                    Revenue = grouped.Sum(line => line.Value * line.Quantity)
                })
                .OrderByDescending(product => product.Revenue)
                .Take(TopProductCount)
                .ToListAsync(cancellation);

            if (ranked.Count == 0)
                return Array.Empty<CustomerTopProductDTO>();

            foreach (var product in ranked)
                product.Revenue = Round(product.Revenue);

            var rankedIds = ranked.Select(product => product.ProductId).ToList();
            var names = (await _productRepository.GetAllAsync(product =>
                    product.EnterpriseId == enterpriseId && rankedIds.Contains(product.Id)))
                .ToDictionary(product => product.Id, product => product.Name);

            foreach (var product in ranked)
                product.ProductName = names.TryGetValue(product.ProductId, out var name) ? name : "Unknown Product";

            return ranked;
        }

        // BRL convention, half away from zero — Math.Round defaults to banker's and would disagree
        // with the enterprise balance.
        private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
