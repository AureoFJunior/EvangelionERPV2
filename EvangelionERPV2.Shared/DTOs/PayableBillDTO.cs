namespace EvangelionERPV2.Shared.DTOs
{
    public class PayableBillDTO : BaseDTO
    {
        public string Description { get; set; } = string.Empty;
        public DateTime DueDate { get; set; } = DateTime.UtcNow;
        public DateTime? PaidAt { get; set; }
        public DateTime? ProductsReceivedAt { get; set; }
        public string? RefundReason { get; set; }
        public DateTime? RefundedAt { get; set; }
        public int BillType { get; set; }
        public decimal Amount { get; set; }
        public bool IsPaid { get; set; }
        public Guid EnterpriseId { get; set; }
        public int? RecurrenceMonths { get; set; }
        public DateTime? RecurrenceUntilUtc { get; set; }

        /// <summary>Set when the sweeper generated this bill from the previous one.</summary>
        public Guid? RecurrenceParentId { get; set; }

        public IEnumerable<PayableBillProductDTO>? Items { get; set; }
    }
}
