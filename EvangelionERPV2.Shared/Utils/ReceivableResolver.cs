using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// The single answer to "what does this order still owe us, and when".
    /// </summary>
    public static class ReceivableResolver
    {
        /// <summary>
        /// The live title per order: the newest one that is not cancelled.
        /// </summary>
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
                        var settledDate = billPaidAt?.Date;
                        return settledDate >= today ? settledDate : null;

                    case BillStatus.Rejected:
                        return null;

                    default:
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
