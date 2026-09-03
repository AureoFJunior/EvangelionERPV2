using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class OrderDTO : BaseDTO
    {
        public DateTime? Payday { get; set; } = null;
        public DateTime? PaymentScheduledDate { get; set; } = null;
        public decimal TotalValue { get; set; } = 0;

        // Cost and margin are Manager-tier and derived, never stored. Null means either "no cost is
        // known for these lines" or "this caller may not see cost" — OrderController nulls them for
        // anyone without products.cost.read. The per-line UnitCost behind them is [JsonIgnore]d.
        public decimal? TotalCost { get; set; } = null;
        public decimal? MarginAmount { get; set; } = null;
        public decimal? MarginPercent { get; set; } = null;

        public Guid? EnterpriseId { get; set; } = null;
        public Guid? CustomerId { get; set; } = null;
        public string? CustomerName { get; set; } = null;
        public IEnumerable<OrderedProduct>? OrderedProduct { get; set; } = null;
        public int Status { get; set; } = 0;

        // EnumPaymentMethod, or Mixed when the sale was split. Null on a backoffice order, which
        // has no tender — see Order.PaymentMethod.
        public int? PaymentMethod { get; set; } = null;

        public string? RefundReason { get; set; } = null;
        public DateTime? RefundedAt { get; set; } = null;

    }
}
