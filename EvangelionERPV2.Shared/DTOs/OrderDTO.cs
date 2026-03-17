using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class OrderDTO : BaseDTO
    {
        public DateTime? Payday { get; set; } = null;
        public DateTime? PaymentScheduledDate { get; set; } = null;
        public double TotalValue { get; set; } = 0;
        public Guid? EnterpriseId { get; set; } = null;
        public Guid? CustomerId { get; set; } = null;
        public string? CustomerName { get; set; } = null;
        public IEnumerable<OrderedProduct>? OrderedProduct { get; set; } = null;
        public int Status { get; set; } = 0;
        public string? RefundReason { get; set; } = null;
        public DateTime? RefundedAt { get; set; } = null;

    }
}
