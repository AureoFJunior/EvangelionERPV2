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
    /// Aging, inadimplência and the per-customer extrato — three views of one question: who owes
    /// what, and for how long.
    /// </summary>
    /// <remarks>
    /// All three read the same position list, built once here, because they must agree: an aging
    /// total that disagrees with the delinquency list is worse than either being absent.
    ///
    /// A receivable is <em>not</em> the same thing as a boleto. `BillsSettings.Enabled` is false in
    /// production, so most tenants have orders and no titles at all — an aging report driven only
    /// by <see cref="Bill"/> rows would show an empty carteira for a company that is owed money.
    /// The order is the receivable; the boleto, where one exists, overrides its date and amount.
    /// </remarks>
    public class ReceivablesReportService : IReceivablesReportService
    {
        private readonly IRepository<Order> _orderRepository;
        private readonly IRepository<Bill> _billRepository;
        private readonly IRepository<Customer> _customerRepository;

        private readonly AppDbContext _context;

        public ReceivablesReportService(
            IRepository<Order> orderRepository,
            IRepository<Bill> billRepository,
            IRepository<Customer> customerRepository,
            AppDbContext context)
        {
            _orderRepository = orderRepository;
            _billRepository = billRepository;
            _customerRepository = customerRepository;
            _context = context;
        }

        public async Task<ReceivablesAgingCustomerDTO> GetCustomerAgingAsync(
            Guid enterpriseId,
            Guid customerId,
            CancellationToken cancellation = default)
        {
            var today = DateTime.UtcNow.Date;

            // The same open-position rule BuildOpenPositionsAsync applies, written as a query: an
            // unsettled order with something still owed, dated by its live title where it has one.
            var positions =
                from order in _context.Set<Order>().AsNoTracking()
                where order.EnterpriseId == enterpriseId
                      && order.CustomerId == customerId
                      && order.IsActive == true
                      && order.TotalValue > 0
                      && order.RefundedAt == null
                      && order.Status != (int)EnumOrderStatus.Refund
                      && order.Status != (int)EnumOrderStatus.Paid
                      && order.Status != (int)EnumOrderStatus.Finished
                let bill = _context.Set<Bill>().AsNoTracking()
                    .Where(candidate => (candidate.IsActive == null || candidate.IsActive == true)
                                        && candidate.OrderId == order.Id
                                        && (candidate.Status == BillStatus.Registered || candidate.Status == BillStatus.Paid))
                    .OrderByDescending(candidate => candidate.CreatedAt)
                    .FirstOrDefault()
                // A settled title means the order is off the carteira, the same skip the loop does.
                where bill == null || bill.Status != BillStatus.Paid
                select new
                {
                    DueDate = bill != null
                        ? bill.DueDate
                        : (order.Payday ?? order.PaymentScheduledDate),
                    // ReceivableResolver.ResolveReceivableAmount, with the Paid branch already
                    // excluded above, so the billed amount wins over the order total.
                    Amount = bill != null && bill.Amount > 0m ? bill.Amount : order.TotalValue
                };

            // The same boundaries as ResolveBucket, expressed as dates rather than as a day count.
            // ResolveBucket asks for (today - DueDate.Date).Days; comparing the column against
            // precomputed cutoffs is the same partition and keeps DueDate seekable, where DATEDIFF
            // or CAST(DueDate AS date) would wrap the column and stop it being. The cutoffs are
            // midnight, so dropping .Date off the column changes nothing.
            var thirtyDaysAgo = today.AddDays(-30);
            var sixtyDaysAgo = today.AddDays(-60);
            var ninetyDaysAgo = today.AddDays(-90);

            var totals = await positions
                .Where(position => position.Amount > 0m)
                .GroupBy(position => 1)
                .Select(grouped => new
                {
                    NotYetDue = grouped.Sum(x => x.DueDate >= today ? x.Amount : 0m),
                    Days1To30 = grouped.Sum(x => x.DueDate < today && x.DueDate >= thirtyDaysAgo ? x.Amount : 0m),
                    Days31To60 = grouped.Sum(x => x.DueDate < thirtyDaysAgo && x.DueDate >= sixtyDaysAgo ? x.Amount : 0m),
                    Days61To90 = grouped.Sum(x => x.DueDate < sixtyDaysAgo && x.DueDate >= ninetyDaysAgo ? x.Amount : 0m),
                    Days90Plus = grouped.Sum(x => x.DueDate < ninetyDaysAgo ? x.Amount : 0m)
                })
                .FirstOrDefaultAsync(cancellation);

            var row = new ReceivablesAgingCustomerDTO { CustomerId = customerId };
            if (totals == null)
                return row;

            row.NotYetDue = totals.NotYetDue;
            row.Days1To30 = totals.Days1To30;
            row.Days31To60 = totals.Days31To60;
            row.Days61To90 = totals.Days61To90;
            row.Days90Plus = totals.Days90Plus;
            row.Total = totals.NotYetDue + totals.Days1To30 + totals.Days31To60 + totals.Days61To90 + totals.Days90Plus;

            return row;
        }

        public async Task<ReceivablesAgingDTO> GetAgingAsync(Guid enterpriseId, CancellationToken cancellation = default)
        {
            var today = DateTime.UtcNow.Date;
            var (positions, _) = await BuildOpenPositionsAsync(enterpriseId);

            var aging = new ReceivablesAgingDTO { GeneratedAtUtc = DateTime.UtcNow };
            var byCustomer = new Dictionary<Guid, ReceivablesAgingCustomerDTO>();

            foreach (var position in positions)
            {
                var daysOverdue = (today - position.DueDate.Date).Days;
                var bucket = ResolveBucket(aging, daysOverdue);

                bucket.Count++;
                bucket.Amount += position.Amount;

                aging.TotalCount++;
                aging.TotalAmount += position.Amount;
                if (daysOverdue > 0)
                    aging.TotalOverdueAmount += position.Amount;

                var key = position.CustomerId ?? Guid.Empty;
                if (!byCustomer.TryGetValue(key, out var customerRow))
                {
                    customerRow = new ReceivablesAgingCustomerDTO
                    {
                        CustomerId = position.CustomerId,
                        CustomerName = position.CustomerName
                    };
                    byCustomer[key] = customerRow;
                }

                AddToCustomerBucket(customerRow, daysOverdue, position.Amount);
                customerRow.Total += position.Amount;
            }

            aging.Customers = byCustomer.Values
                .OrderByDescending(x => x.Total)
                .ToList();

            return aging;
        }

        public async Task<IReadOnlyList<DelinquentCustomerDTO>> GetDelinquentAsync(
            Guid enterpriseId,
            int minimumDaysOverdue,
            CancellationToken cancellation = default)
        {
            var today = DateTime.UtcNow.Date;
            var threshold = Math.Max(1, minimumDaysOverdue);
            var (positions, customersById) = await BuildOpenPositionsAsync(enterpriseId);

            var overdue = positions
                .Select(position => new { position, days = (today - position.DueDate.Date).Days })
                .Where(x => x.days >= threshold)
                .ToList();

            if (overdue.Count == 0)
                return [];

            return overdue
                .GroupBy(x => x.position.CustomerId ?? Guid.Empty)
                .Select(group =>
                {
                    var contact = customersById.GetValueOrDefault(group.Key);
                    var oldest = group.Min(x => x.position.DueDate.Date);

                    return new DelinquentCustomerDTO
                    {
                        CustomerId = group.First().position.CustomerId,
                        CustomerName = group.First().position.CustomerName,
                        Email = contact?.Email ?? string.Empty,
                        PhoneNumber = contact?.PhoneNumber ?? string.Empty,
                        OverdueCount = group.Count(),
                        OverdueAmount = group.Sum(x => x.position.Amount),
                        OldestDueDate = oldest,
                        DaysOverdue = (today - oldest).Days
                    };
                })
                .OrderByDescending(x => x.OverdueAmount)
                .ToList();
        }

        public async Task<CustomerStatementDTO?> GetStatementAsync(
            Guid enterpriseId,
            Guid customerId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellation = default)
        {
            if (customerId == Guid.Empty)
                return null;

            // GetPagedAsync, not GetAllAsync: the override throws when nothing matches, and an
            // extrato for an unknown customer must answer "no such customer", not blow up.
            var customer = (await _customerRepository.GetPagedAsync(
                    x => x.Id == customerId && x.EnterpriseId == enterpriseId && x.IsActive == true))
                .Items
                .FirstOrDefault();

            if (customer == null)
                return null;

            var from = fromUtc.Date;
            var to = toUtc.Date;
            if (to < from)
                (from, to) = (to, from);

            // One customer, so the whole history is a bounded read and the opening balance can be
            // computed from it rather than stored.
            var orders = (await _orderRepository.GetPagedAsync(
                    x => x.EnterpriseId == enterpriseId && x.CustomerId == customerId && x.IsActive == true))
                .Items;

            var liveBills = ReceivableResolver.SelectLiveBillsByOrder(
                await _billRepository.GetAllAsync(
                    x => x.Order != null
                         && x.Order.EnterpriseId == enterpriseId
                         && x.Order.CustomerId == customerId
                         && (x.IsActive == null || x.IsActive == true)));

            var entries = new List<CustomerStatementEntryDTO>();

            foreach (var order in orders)
            {
                var bill = liveBills.GetValueOrDefault(order.Id);
                var amount = ReceivableResolver.ResolveReceivableAmount(order, bill);
                if (amount <= 0)
                    continue;

                entries.Add(new CustomerStatementEntryDTO
                {
                    Date = order.CreatedAt.Date,
                    Kind = "order",
                    Description = bill == null ? "Order" : $"Order · boleto {bill.DocumentNumber}",
                    OrderId = order.Id,
                    Debit = amount
                });

                if (ReceivableResolver.IsOrderRefunded(order))
                {
                    entries.Add(new CustomerStatementEntryDTO
                    {
                        Date = (order.RefundedAt ?? order.UpdatedAt ?? order.CreatedAt).Date,
                        Kind = "refund",
                        Description = string.IsNullOrWhiteSpace(order.RefundReason) ? "Refund" : $"Refund · {order.RefundReason}",
                        OrderId = order.Id,
                        Credit = amount
                    });

                    continue;
                }

                if (bill is { Status: BillStatus.Paid, PaidAt: not null })
                {
                    entries.Add(new CustomerStatementEntryDTO
                    {
                        Date = bill.PaidAt!.Value.Date,
                        Kind = "payment",
                        Description = $"Payment · boleto {bill.DocumentNumber}",
                        OrderId = order.Id,
                        Credit = bill.PaidAmount is > 0 ? bill.PaidAmount.Value : bill.Amount
                    });
                }
            }

            var ordered = entries.OrderBy(x => x.Date).ThenBy(x => x.Kind).ToList();

            var statement = new CustomerStatementDTO
            {
                CustomerId = customerId,
                CustomerName = customer.Name,
                FromUtc = from,
                ToUtc = to,
                OpeningBalance = ordered.Where(x => x.Date < from).Sum(x => x.Debit - x.Credit)
            };

            var runningBalance = statement.OpeningBalance;
            foreach (var entry in ordered.Where(x => x.Date >= from && x.Date <= to))
            {
                runningBalance += entry.Debit - entry.Credit;
                entry.RunningBalance = runningBalance;
                statement.Entries.Add(entry);
            }

            statement.ClosingBalance = runningBalance;
            return statement;
        }

        /// <summary>
        /// Everything still owed, one row per open order.
        /// </summary>
        /// <remarks>
        /// Two bounded queries and no id list: orders are filtered to the unsettled ones in SQL,
        /// and bills are scoped through the Order navigation. Collecting order ids to feed a
        /// <c>Contains</c> would read the whole Order table and break past SQL Server's ~2,100
        /// parameter limit on a tenant with enough history.
        /// </remarks>
        private async Task<(List<ReceivablePosition> Positions, Dictionary<Guid, Customer> CustomersById)>
            BuildOpenPositionsAsync(Guid enterpriseId)
        {
            // GetPagedAsync deliberately, not GetAllAsync: OrderRepository overrides GetAllAsync to
            // Include Customer, OrderedProduct and Product, which for a carteira report means
            // dragging every line item of every open order across the wire to read four columns.
            var openOrders = (await _orderRepository.GetPagedAsync(
                    x => x.EnterpriseId == enterpriseId
                         && x.IsActive == true
                         && x.TotalValue > 0
                         && x.RefundedAt == null
                         && x.Status != (int)EnumOrderStatus.Refund
                         && x.Status != (int)EnumOrderStatus.Paid
                         && x.Status != (int)EnumOrderStatus.Finished))
                .Items;

            // ponytail: one read of the tenant's customer master per report. Project it in the
            // repository if a tenant's customer list ever makes this the expensive half.
            //
            // GetPagedAsync, not GetAllAsync: the override throws on an empty result, so a tenant
            // with no customers yet would get a 204 instead of an empty carteira.
            var customersById = (await _customerRepository.GetPagedAsync(
                    x => x.EnterpriseId == enterpriseId && x.IsActive == true))
                .Items
                .ToDictionary(x => x.Id, x => x);

            if (openOrders.Count == 0)
                return ([], customersById);

            // Paid titles are fetched too, and only to exclude their order: settling a boleto does
            // not move Order.Status, so the title is the only place that settlement is recorded.
            var liveBills = ReceivableResolver.SelectLiveBillsByOrder(
                await _billRepository.GetAllAsync(
                    x => x.Order != null
                         && x.Order.EnterpriseId == enterpriseId
                         && (x.IsActive == null || x.IsActive == true)
                         && (x.Status == BillStatus.Registered || x.Status == BillStatus.Paid)));

            var positions = new List<ReceivablePosition>();

            foreach (var order in openOrders)
            {
                var bill = liveBills.GetValueOrDefault(order.Id);
                if (bill is { Status: BillStatus.Paid })
                    continue;

                var amount = ReceivableResolver.ResolveReceivableAmount(order, bill);
                if (amount <= 0)
                    continue;

                var customer = order.CustomerId.HasValue
                    ? customersById.GetValueOrDefault(order.CustomerId.Value)
                    : null;

                positions.Add(new ReceivablePosition
                {
                    OrderId = order.Id,
                    CustomerId = order.CustomerId,
                    CustomerName = customer?.Name ?? "—",
                    DueDate = bill?.DueDate ?? order.Payday ?? order.PaymentScheduledDate,
                    Amount = amount,
                    DocumentNumber = bill?.DocumentNumber ?? string.Empty
                });
            }

            return (positions, customersById);
        }

        private static ReceivablesAgingBucketDTO ResolveBucket(ReceivablesAgingDTO aging, int daysOverdue) => daysOverdue switch
        {
            <= 0 => aging.NotYetDue,
            <= 30 => aging.Days1To30,
            <= 60 => aging.Days31To60,
            <= 90 => aging.Days61To90,
            _ => aging.Days90Plus
        };

        private static void AddToCustomerBucket(ReceivablesAgingCustomerDTO row, int daysOverdue, decimal amount)
        {
            switch (daysOverdue)
            {
                case <= 0: row.NotYetDue += amount; break;
                case <= 30: row.Days1To30 += amount; break;
                case <= 60: row.Days31To60 += amount; break;
                case <= 90: row.Days61To90 += amount; break;
                default: row.Days90Plus += amount; break;
            }
        }

        private sealed class ReceivablePosition
        {
            public Guid OrderId { get; init; }
            public Guid? CustomerId { get; init; }
            public string CustomerName { get; init; } = string.Empty;
            public DateTime DueDate { get; init; }
            public decimal Amount { get; init; }
            public string DocumentNumber { get; init; } = string.Empty;
        }
    }
}
