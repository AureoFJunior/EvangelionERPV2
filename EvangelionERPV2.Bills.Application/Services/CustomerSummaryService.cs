using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;

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

        public CustomerSummaryService(
            IRepository<Customer> customerRepository,
            IRepository<Order> orderRepository,
            IRepository<Bill> billRepository,
            IRepository<OrderedProduct> orderedProductRepository,
            IRepository<Product> productRepository,
            IReceivablesReportService receivablesReportService)
        {
            _customerRepository = customerRepository;
            _orderRepository = orderRepository;
            _billRepository = billRepository;
            _orderedProductRepository = orderedProductRepository;
            _productRepository = productRepository;
            _receivablesReportService = receivablesReportService;
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

            var orders = (await _orderRepository.GetAllAsync(order =>
                    order.IsActive == true
                    && order.EnterpriseId == enterpriseId
                    && order.CustomerId == customerId))
                .OrderByDescending(order => order.CreatedAt)
                .ToList();

            var orderIds = orders.Select(order => order.Id).ToList();

            // Bill carries no EnterpriseId — it is tenant-scoped through Order, which is why this
            // is keyed by the order ids already filtered above.
            var bills = orderIds.Count == 0
                ? new List<Bill>()
                : (await _billRepository.GetAllAsync(bill => bill.IsActive == true && orderIds.Contains(bill.OrderId))).ToList();

            var liveBills = ReceivableResolver.SelectLiveBillsByOrder(bills);

            // Refunds are excluded from what the customer "bought": the money went back.
            var realOrders = orders.Where(order => !ReceivableResolver.IsOrderRefunded(order)).ToList();
            var totalPurchased = Round(realOrders.Sum(order => order.TotalValue));

            var summary = new CustomerSummaryDTO
            {
                CustomerId = customer.Id,
                CustomerName = customer.Name,
                Email = customer.Email,
                PhoneNumber = customer.PhoneNumber,
                Document = customer.Document,
                Municipio = customer.Municipio,
                Uf = customer.Uf,
                OrderCount = realOrders.Count,
                TotalPurchased = totalPurchased,
                AverageTicket = realOrders.Count > 0 ? Round(totalPurchased / realOrders.Count) : null,
                FirstPurchaseAtUtc = realOrders.Count > 0 ? realOrders.Min(order => order.CreatedAt) : null,
                LastPurchaseAtUtc = realOrders.Count > 0 ? realOrders.Max(order => order.CreatedAt) : null,
                RecentOrders = orders.Take(RecentOrderCount).Select(order => new CustomerOrderSummaryDTO
                {
                    OrderId = order.Id,
                    CreatedAtUtc = order.CreatedAt,
                    TotalValue = order.TotalValue,
                    Status = order.Status,
                    IsRefunded = ReceivableResolver.IsOrderRefunded(order),
                    BillStatus = liveBills.TryGetValue(order.Id, out var bill) ? (int)bill.Status : null,
                    BillDueDate = liveBills.TryGetValue(order.Id, out var dueBill) ? dueBill.DueDate : null
                }).ToList(),
                TopProducts = await BuildTopProductsAsync(realOrders.Select(order => order.Id).ToList(), enterpriseId)
            };

            if (summary.LastPurchaseAtUtc.HasValue)
                summary.DaysSinceLastPurchase = (DateTime.UtcNow.Date - summary.LastPurchaseAtUtc.Value.Date).Days;

            if (includeFinancial)
                summary.Financial = await BuildFinancialAsync(customer, realOrders, liveBills, enterpriseId, cancellation);

            return summary;
        }

        private async Task<CustomerFinancialSummaryDTO> BuildFinancialAsync(
            Customer customer,
            IEnumerable<Order> orders,
            IReadOnlyDictionary<Guid, Bill> liveBills,
            Guid enterpriseId,
            CancellationToken cancellation)
        {
            var exposure = Round(orders.Sum(order =>
                ReceivableResolver.ResolveOpenExposure(order, liveBills.TryGetValue(order.Id, out var bill) ? bill : null)));

            var financial = new CustomerFinancialSummaryDTO
            {
                OpenExposure = exposure,
                CreditLimit = customer.CreditLimit,
                // Null, not zero, when no limit is set: an unlimited customer has no "available"
                // figure, and showing zero would read as "blocked".
                CreditAvailable = customer.CreditLimit.HasValue ? Round(customer.CreditLimit.Value - exposure) : null
            };

            // The aging rule is reused rather than repeated — one opinion about which bucket a
            // receivable falls in. ponytail: this scans the tenant's whole open carteira to read one
            // customer's row; give ReceivablesReportService a customer filter if a big tenant feels it.
            var aging = await _receivablesReportService.GetAgingAsync(enterpriseId, cancellation);
            var row = aging.Customers.FirstOrDefault(entry => entry.CustomerId == customer.Id);

            if (row != null)
            {
                financial.NotYetDue = row.NotYetDue;
                financial.Days1To30 = row.Days1To30;
                financial.Days31To60 = row.Days31To60;
                financial.Days61To90 = row.Days61To90;
                financial.Days90Plus = row.Days90Plus;
                financial.OverdueAmount = Round(row.Days1To30 + row.Days31To60 + row.Days61To90 + row.Days90Plus);
            }

            return financial;
        }

        private async Task<IReadOnlyList<CustomerTopProductDTO>> BuildTopProductsAsync(
            IReadOnlyList<Guid> orderIds,
            Guid enterpriseId)
        {
            if (orderIds.Count == 0)
                return Array.Empty<CustomerTopProductDTO>();

            var lines = (await _orderedProductRepository.GetAllAsync(line =>
                    line.IsActive == true && line.OrderId.HasValue && orderIds.Contains(line.OrderId.Value)))
                .Where(line => line.Quantity > 0)
                .ToList();

            if (lines.Count == 0)
                return Array.Empty<CustomerTopProductDTO>();

            var ranked = lines
                .GroupBy(line => line.ProductId)
                .Select(group => new CustomerTopProductDTO
                {
                    ProductId = group.Key,
                    Quantity = group.Sum(line => line.Quantity),
                    Revenue = Round(group.Sum(line => line.Value * line.Quantity))
                })
                .OrderByDescending(product => product.Revenue)
                .Take(TopProductCount)
                .ToList();

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
