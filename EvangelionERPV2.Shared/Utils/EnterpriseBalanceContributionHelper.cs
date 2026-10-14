using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Shared.Utils
{
    public static class EnterpriseBalanceContributionHelper
    {
        public static double GetOrderRealizedContribution(Order? order, DateTime today)
        {
            if (order == null || order.IsActive != true)
                return 0;

            if (order.TotalValue <= 0 || IsOrderRefunded(order))
                return 0;

            var realizedDate = ResolveRealizedOrderDate(order);
            if (!realizedDate.HasValue || realizedDate.Value.Date > today.Date)
                return 0;

            return order.TotalValue;
        }

        public static double GetPayableRealizedContribution(PayableBill? payableBill, DateTime today)
        {
            if (payableBill == null || payableBill.IsActive != true)
                return 0;

            if (payableBill.Amount <= 0 || payableBill.RefundedAt.HasValue)
                return 0;

            var realizedDate = ResolveRealizedPayableDate(payableBill);
            if (!realizedDate.HasValue || realizedDate.Value.Date > today.Date)
                return 0;

            return -Math.Abs(payableBill.Amount);
        }

        private static DateTime? ResolveRealizedOrderDate(Order order)
        {
            if (order.Payday.HasValue)
                return order.Payday.Value.Date;

            if (order.Status == (int)EnumOrderStatus.Paid
                || order.Status == (int)EnumOrderStatus.Shipped
                || order.Status == (int)EnumOrderStatus.Delivered
                || order.Status == (int)EnumOrderStatus.Finished)
            {
                return order.PaymentScheduledDate.Date;
            }

            return null;
        }

        private static DateTime? ResolveRealizedPayableDate(PayableBill payableBill)
        {
            if (!payableBill.IsPaid)
                return null;

            if (payableBill.PaidAt.HasValue)
                return payableBill.PaidAt.Value.Date;

            return payableBill.DueDate.Date;
        }

        private static bool IsOrderRefunded(Order order)
        {
            return order.RefundedAt.HasValue || order.Status == (int)EnumOrderStatus.Refund;
        }
    }
}
