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

        /// <summary>
        /// The tenant's open carteira as a query: one row per unsettled order, dated by its live
        /// title where it has one, with what is still owed on it.
        /// </summary>
        /// <remarks>
        /// The same rule the in-memory builder applied, kept as an <see cref="IQueryable{T}"/> so
        /// the callers can aggregate it in SQL instead of materialising a position per open order.
        /// Building that list cost 1.3 GiB on a tenant with 1.8M of them, which is two concurrent
        /// report views away from taking a 2 GB container down.
        /// </remarks>
        private IQueryable<OpenPosition> OpenPositions(Guid enterpriseId, Guid? customerId = null)
        {
            var positions =
                from order in _context.Set<Order>().AsNoTracking()
                where order.EnterpriseId == enterpriseId
                      && order.IsActive == true
                      && order.TotalValue > 0m
                      && order.RefundedAt == null
                      && order.Status != (int)EnumOrderStatus.Refund
                      && order.Status != (int)EnumOrderStatus.Paid
                      && order.Status != (int)EnumOrderStatus.Finished
                      && (customerId == null || order.CustomerId == customerId)
                let bill = _context.Set<Bill>().AsNoTracking()
                    .Where(candidate => (candidate.IsActive == null || candidate.IsActive == true)
                                        && candidate.OrderId == order.Id
                                        && (candidate.Status == BillStatus.Registered || candidate.Status == BillStatus.Paid))
                    .OrderByDescending(candidate => candidate.CreatedAt)
                    .FirstOrDefault()
                // A settled title means the order is off the carteira.
                where bill == null || bill.Status != BillStatus.Paid
                select new OpenPosition
                {
                    CustomerId = order.CustomerId,
                    DueDate = bill != null ? bill.DueDate : (order.Payday ?? order.PaymentScheduledDate),
                    // ReceivableResolver.ResolveReceivableAmount with the Paid branch already
                    // excluded, so the billed amount wins over the order total.
                    Amount = bill != null && bill.Amount > 0m ? bill.Amount : order.TotalValue
                };

            return positions.Where(position => position.Amount > 0m);
        }

        /// <summary>
        /// The aging bucket boundaries as dates rather than as a day count.
        /// </summary>
        /// <remarks>
        /// The buckets used to be picked by <c>(today - DueDate.Date).Days</c> per row; comparing
        /// the column against precomputed cutoffs is the same partition, and it keeps DueDate
        /// seekable where DATEDIFF or
        /// CAST(DueDate AS date) would wrap the column and stop it being. The cutoffs are midnight,
        /// so dropping <c>.Date</c> off the column changes nothing.
        /// </remarks>
        private readonly record struct BucketCutoffs(DateTime Today, DateTime Days30, DateTime Days60, DateTime Days90)
        {
            public static BucketCutoffs From(DateTime today) =>
                new(today, today.AddDays(-30), today.AddDays(-60), today.AddDays(-90));
        }


        public async Task<ReceivablesAgingCustomerDTO> GetCustomerAgingAsync(
            Guid enterpriseId,
            Guid customerId,
            CancellationToken cancellation = default)
        {
            var cut = BucketCutoffs.From(DateTime.UtcNow.Date);

            // Conditional sums rather than a grouping on the bucket, for the reason GetAgingAsync
            // gives: grouping on a computed key makes SQL Server sort every row.
            var totals = await OpenPositions(enterpriseId, customerId)
                .GroupBy(position => 1)
                .Select(grouped => new
                {
                    NotYetDue = grouped.Sum(x => x.DueDate >= cut.Today ? x.Amount : 0m),
                    Days1To30 = grouped.Sum(x => x.DueDate < cut.Today && x.DueDate >= cut.Days30 ? x.Amount : 0m),
                    Days31To60 = grouped.Sum(x => x.DueDate < cut.Days30 && x.DueDate >= cut.Days60 ? x.Amount : 0m),
                    Days61To90 = grouped.Sum(x => x.DueDate < cut.Days60 && x.DueDate >= cut.Days90 ? x.Amount : 0m),
                    Days90Plus = grouped.Sum(x => x.DueDate < cut.Days90 ? x.Amount : 0m)
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
            var cut = BucketCutoffs.From(DateTime.UtcNow.Date);

            // Grouped by customer *and* bucket, so the database returns at most five rows per
            // customer instead of one per open order. Bucketing once per row and grouping on the
            // result beats ten conditional aggregates over the same rows: the first version of this
            // wrote SUM(CASE) and COUNT(CASE) five times each and took 13.8s against 2.9s here.
            // Grouped by customer, with the buckets as conditional sums inside that grouping.
            //
            // Two shapes were measured against 1.84M open positions and both were worse. Grouping
            // on a computed bucket key — GroupBy(new { CustomerId, CASE ... }) — makes SQL Server
            // sort every row and the request passed the 30s timeout. Building a position object per
            // open order, which is what this method used to do, cost 1.285 GiB and is two concurrent
            // report views from taking a 2 GB container down. This shape is 13.8s and 186 MiB.
            var rows = await OpenPositions(enterpriseId)
                .GroupBy(position => position.CustomerId)
                .Select(grouped => new
                {
                    CustomerId = grouped.Key,
                    NotYetDue = grouped.Sum(x => x.DueDate >= cut.Today ? x.Amount : 0m),
                    NotYetDueCount = grouped.Count(x => x.DueDate >= cut.Today),
                    Days1To30 = grouped.Sum(x => x.DueDate < cut.Today && x.DueDate >= cut.Days30 ? x.Amount : 0m),
                    Days1To30Count = grouped.Count(x => x.DueDate < cut.Today && x.DueDate >= cut.Days30),
                    Days31To60 = grouped.Sum(x => x.DueDate < cut.Days30 && x.DueDate >= cut.Days60 ? x.Amount : 0m),
                    Days31To60Count = grouped.Count(x => x.DueDate < cut.Days30 && x.DueDate >= cut.Days60),
                    Days61To90 = grouped.Sum(x => x.DueDate < cut.Days60 && x.DueDate >= cut.Days90 ? x.Amount : 0m),
                    Days61To90Count = grouped.Count(x => x.DueDate < cut.Days60 && x.DueDate >= cut.Days90),
                    Days90Plus = grouped.Sum(x => x.DueDate < cut.Days90 ? x.Amount : 0m),
                    Days90PlusCount = grouped.Count(x => x.DueDate < cut.Days90)
                })
                .ToListAsync(cancellation);

            var customersById = await ReadCustomersAsync(enterpriseId);
            var aging = new ReceivablesAgingDTO { GeneratedAtUtc = DateTime.UtcNow };

            foreach (var row in rows)
            {
                Accumulate(aging.NotYetDue, row.NotYetDue, row.NotYetDueCount);
                Accumulate(aging.Days1To30, row.Days1To30, row.Days1To30Count);
                Accumulate(aging.Days31To60, row.Days31To60, row.Days31To60Count);
                Accumulate(aging.Days61To90, row.Days61To90, row.Days61To90Count);
                Accumulate(aging.Days90Plus, row.Days90Plus, row.Days90PlusCount);
            }

            aging.TotalCount = aging.NotYetDue.Count + aging.Days1To30.Count + aging.Days31To60.Count
                               + aging.Days61To90.Count + aging.Days90Plus.Count;
            aging.TotalAmount = aging.NotYetDue.Amount + aging.Days1To30.Amount + aging.Days31To60.Amount
                                + aging.Days61To90.Amount + aging.Days90Plus.Amount;
            // Overdue is everything past its due date, which is every bucket but the first.
            aging.TotalOverdueAmount = aging.Days1To30.Amount + aging.Days31To60.Amount
                                       + aging.Days61To90.Amount + aging.Days90Plus.Amount;

            aging.Customers = rows
                .Select(row => new ReceivablesAgingCustomerDTO
                {
                    CustomerId = row.CustomerId,
                    CustomerName = CustomerName(customersById, row.CustomerId),
                    NotYetDue = row.NotYetDue,
                    Days1To30 = row.Days1To30,
                    Days31To60 = row.Days31To60,
                    Days61To90 = row.Days61To90,
                    Days90Plus = row.Days90Plus,
                    Total = row.NotYetDue + row.Days1To30 + row.Days31To60 + row.Days61To90 + row.Days90Plus
                })
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

            // (today - DueDate.Date).Days >= threshold, as a date the column can be compared to.
            var cutoffExclusive = today.AddDays(-threshold + 1);

            var rows = await OpenPositions(enterpriseId)
                .Where(position => position.DueDate < cutoffExclusive)
                .GroupBy(position => position.CustomerId)
                .Select(grouped => new
                {
                    CustomerId = grouped.Key,
                    OverdueCount = grouped.Count(),
                    OverdueAmount = grouped.Sum(x => x.Amount),
                    OldestDueDate = grouped.Min(x => x.DueDate)
                })
                .ToListAsync(cancellation);

            if (rows.Count == 0)
                return [];

            var customersById = await ReadCustomersAsync(enterpriseId);

            return rows
                .Select(row =>
                {
                    var contact = customersById.GetValueOrDefault(row.CustomerId ?? Guid.Empty);
                    // Min of the raw column then truncated is the same day as the min of the
                    // truncated values, so this matches what the per-row version produced.
                    var oldest = row.OldestDueDate.Date;

                    return new DelinquentCustomerDTO
                    {
                        CustomerId = row.CustomerId,
                        CustomerName = CustomerName(customersById, row.CustomerId),
                        Email = contact?.Email ?? string.Empty,
                        PhoneNumber = contact?.PhoneNumber ?? string.Empty,
                        OverdueCount = row.OverdueCount,
                        OverdueAmount = row.OverdueAmount,
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
        /// <summary>
        /// The tenant's customer master, for names and contact details on the report rows.
        /// </summary>
        /// <remarks>
        /// GetPagedAsync, not GetAllAsync: the override throws on an empty result, so a tenant with
        /// no customers yet would get a 204 instead of an empty carteira. ponytail: one read of the
        /// whole customer list per report — project it in the repository if a tenant's customer
        /// list ever makes this the expensive half. It is bounded by customers, not by orders,
        /// which is the part that mattered.
        /// </remarks>
        private async Task<Dictionary<Guid, Customer>> ReadCustomersAsync(Guid enterpriseId)
        {
            return (await _customerRepository.GetPagedAsync(
                    x => x.EnterpriseId == enterpriseId && x.IsActive == true))
                .Items
                .ToDictionary(x => x.Id, x => x);
        }

        private static string CustomerName(IReadOnlyDictionary<Guid, Customer> customersById, Guid? customerId)
        {
            return customerId.HasValue && customersById.TryGetValue(customerId.Value, out var customer)
                ? customer.Name
                : "—";
        }

        private static void Accumulate(ReceivablesAgingBucketDTO bucket, decimal amount, int count)
        {
            bucket.Amount += amount;
            bucket.Count += count;
        }

        /// <summary>One unsettled order: who owes it, when it is due, and how much is left on it.</summary>
        private sealed class OpenPosition
        {
            public Guid? CustomerId { get; init; }
            public DateTime DueDate { get; init; }
            public decimal Amount { get; init; }
        }

    }
}
