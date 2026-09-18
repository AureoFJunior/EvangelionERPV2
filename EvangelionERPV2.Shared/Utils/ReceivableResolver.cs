using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Shared.Utils
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
    ///
    /// It lives in Shared rather than in Bills because the credit-limit guard on order creation
    /// needs the same exposure figure, and one module reaching into another's services to ask what
    /// a customer owes is how a second copy of this rule gets written.
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
            return ResolveOrderReceivableDate(
                order.Payday,
                order.PaymentScheduledDate,
                bill?.Status,
                bill?.PaidAt,
                bill?.DueDate,
                today);
        }

        /// <summary>
        /// The same rule over plain values, so a caller that projected only the columns it needs
        /// does not have to restate it.
        /// </summary>
        /// <remarks>
        /// The cash-flow forecast reads this way: materialising whole Order entities for every
        /// order inside the horizon is what made a 1.84M-order tenant expensive even after the
        /// window moved into SQL. <paramref name="billStatus"/> is null when the order has no live
        /// title, which is what selects the order's own dates.
        /// </remarks>
        public static DateTime? ResolveOrderReceivableDate(
            DateTime? orderPayday,
            DateTime orderPaymentScheduledDate,
            BillStatus? billStatus,
            DateTime? billPaidAt,
            DateTime? billDueDate,
            DateTime today)
        {
            if (billStatus.HasValue)
            {
                switch (billStatus.Value)
                {
                    case BillStatus.Paid:
                        // Same rule the payables side uses: cash that landed before today is already
                        // inside the current balance and must not be counted a second time.
                        var settledDate = billPaidAt?.Date;
                        return settledDate >= today ? settledDate : null;

                    case BillStatus.Rejected:
                        // The bank refused the title. There is no receivable until it is reissued.
                        return null;

                    default:
                        // Open, overdue included. Past due is dropped rather than pulled forward:
                        // a boleto nobody paid on time is not evidence of cash arriving today, and
                        // the receivables side of this forecast is deliberately the conservative one.
                        var dueDate = billDueDate!.Value.Date;
                        return dueDate < today ? null : dueDate;
                }
            }

            if (orderPayday.HasValue)
            {
                var paidDate = orderPayday.Value.Date;
                return paidDate >= today ? paidDate : null;
            }

            var scheduledDate = orderPaymentScheduledDate.Date;
            // When using persisted current balance, realized receivables in the past must not be projected again.
            return scheduledDate < today ? null : scheduledDate;
        }

        /// <summary>
        /// What actually arrives. A settled boleto reports what the bank credited, which is not
        /// always the order total — a payer settles late with interest, or short.
        /// </summary>
        public static decimal ResolveReceivableAmount(Order order, Bill? bill)
        {
            return ResolveReceivableAmount(
                order.TotalValue,
                bill?.Status,
                bill?.PaidAmount,
                bill?.Amount);
        }

        /// <summary>
        /// The same rule over plain values rather than entities, so a caller that has projected only
        /// the columns it needs does not have to restate the rule.
        /// <para>
        /// This exists because SalesPerformance used to materialise a whole month of Order and Bill
        /// entities to compute a handful of totals. Reading only the columns the rule touches is
        /// what made that affordable - and the entity overloads above delegate here, so there is
        /// still exactly one definition of what money has been received.
        /// </para>
        /// </summary>
        public static decimal ResolveReceivableAmount(
            decimal orderTotalValue,
            BillStatus? billStatus,
            decimal? billPaidAmount,
            decimal? billAmount)
        {
            if (billStatus == BillStatus.Paid && billPaidAmount > 0)
                return billPaidAmount!.Value;

            return billAmount > 0 ? billAmount!.Value : orderTotalValue;
        }

        /// <summary>
        /// Value-based twin of <see cref="ResolveReceivedAmount(Order, Bill?)"/>.
        /// </summary>
        public static decimal ResolveReceivedAmount(
            decimal orderTotalValue,
            bool orderIsRefunded,
            bool orderHasPayday,
            BillStatus? billStatus,
            decimal? billPaidAmount,
            decimal? billAmount)
        {
            if (orderIsRefunded)
                return 0;

            if (billStatus.HasValue)
            {
                return billStatus == BillStatus.Paid
                    ? ResolveReceivableAmount(orderTotalValue, billStatus, billPaidAmount, billAmount)
                    : 0;
            }

            return orderHasPayday ? orderTotalValue : 0;
        }

        /// <summary>
        /// Value-based twin of <see cref="IsOrderRefunded(Order)"/>.
        /// </summary>
        public static bool IsOrderRefunded(DateTime? refundedAt, int status)
        {
            return refundedAt.HasValue || status == (int)EnumOrderStatus.Refund;
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

        /// <summary>
        /// What has actually been collected on this order. The mirror of
        /// <see cref="ResolveOpenExposure"/>: exposure is what is still owed, this is what came in.
        /// </summary>
        /// <remarks>
        /// Commission is paid on this, not on the order total. Paying a seller for an invoice
        /// nobody has settled is how a small company ends up funding its own bad debt.
        ///
        /// An order with a boleto is answered by the boleto — an unsettled one has collected
        /// nothing however old it is. An order without one falls back to Payday, which OrderService
        /// only ever sets for a status that means the money arrived.
        /// </remarks>
        public static decimal ResolveReceivedAmount(Order order, Bill? bill)
        {
            return ResolveReceivedAmount(
                order.TotalValue,
                IsOrderRefunded(order),
                order.Payday.HasValue,
                bill?.Status,
                bill?.PaidAmount,
                bill?.Amount);
        }

        public static bool IsOrderRefunded(Order order)
        {
            return IsOrderRefunded(order.RefundedAt, order.Status);
        }
    }
}
