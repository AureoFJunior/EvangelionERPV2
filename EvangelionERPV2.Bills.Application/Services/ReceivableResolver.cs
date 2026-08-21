using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    /// <summary>
    /// The single answer to "what does this order still owe us, and when".
    /// </summary>
    /// <remarks>
    /// It lives on its own because four readers need the same answer — the cash-flow forecast, the
    /// daily panel, the receivables aging report and the credit-limit check — and a second copy of
    /// this rule would be a second opinion about money. The rule itself: where an order has a
    /// boleto, the boleto is the truth about the receivable; the order's own dates only ever said
    /// when we <em>hoped</em> to be paid.
    /// </remarks>
    public static class ReceivableResolver
    {
        /// <summary>
        /// The live title per order: the newest one that is not cancelled.
        /// </summary>
        /// <remarks>
        /// Cancelled is skipped so a reissued order resolves to the title the payer is actually
        /// holding. Callers scope the query by order id because <see cref="Bill"/> carries no
        /// <c>EnterpriseId</c> of its own — it is tenant-scoped through <see cref="Order"/>.
        /// </remarks>
        public static Dictionary<Guid, Bill> SelectLiveBillsByOrder(IEnumerable<Bill> bills)
        {
            return bills
                .Where(bill => bill.Status != BillStatus.Cancelled)
                .GroupBy(bill => bill.OrderId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(bill => bill.CreatedAt).First());
        }

        /// <summary>
        /// When the money is expected, or null when this order projects no cash from today on.
        /// </summary>
        /// <remarks>
        /// The boleto branch is the point of this method. Before it existed the forecast could not
        /// tell an issued boleto from a paid one: <c>Order.Payday</c> is the date fed to the boleto
        /// as its <em>vencimento</em>, so merely printing a document made the money look received.
        /// Now an unsettled title only ever projects on its own due date, and a settled one
        /// projects on the day the money actually landed.
        /// </remarks>
        public static DateTime? ResolveOrderReceivableDate(Order order, Bill? bill, DateTime today)
        {
            if (bill != null)
            {
                switch (bill.Status)
                {
                    case BillStatus.Paid:
                        // Same rule the payables side uses: cash that landed before today is already
                        // inside the current balance and must not be counted a second time.
                        var settledDate = bill.PaidAt?.Date;
                        return settledDate >= today ? settledDate : null;

                    case BillStatus.Rejected:
                        // The bank refused the title. There is no receivable until it is reissued.
                        return null;

                    default:
                        // Open, overdue included. Past due is dropped rather than pulled forward:
                        // a boleto nobody paid on time is not evidence of cash arriving today, and
                        // the receivables side of this forecast is deliberately the conservative one.
                        var dueDate = bill.DueDate.Date;
                        return dueDate < today ? null : dueDate;
                }
            }

            if (order.Payday.HasValue)
            {
                var paidDate = order.Payday.Value.Date;
                return paidDate >= today ? paidDate : null;
            }

            var scheduledDate = order.PaymentScheduledDate.Date;
            // When using persisted current balance, realized receivables in the past must not be projected again.
            return scheduledDate < today ? null : scheduledDate;
        }

        /// <summary>
        /// What actually arrives. A settled boleto reports what the bank credited, which is not
        /// always the order total — a payer settles late with interest, or short.
        /// </summary>
        public static decimal ResolveReceivableAmount(Order order, Bill? bill)
        {
            if (bill is { Status: BillStatus.Paid, PaidAmount: > 0 })
                return bill.PaidAmount.Value;

            return bill?.Amount > 0 ? bill.Amount : order.TotalValue;
        }

        /// <summary>
        /// What is still owed on an order, ignoring dates: zero once the title is settled or the
        /// order was refunded. This is the exposure figure, not the forecast figure.
        /// </summary>
        public static decimal ResolveOpenExposure(Order order, Bill? bill)
        {
            if (IsOrderRefunded(order))
                return 0;

            if (bill is { Status: BillStatus.Paid })
                return 0;

            return ResolveReceivableAmount(order, bill);
        }

        public static bool IsOrderRefunded(Order order)
        {
            return order.RefundedAt.HasValue || order.Status == (int)EnumOrderStatus.Refund;
        }
    }
}
