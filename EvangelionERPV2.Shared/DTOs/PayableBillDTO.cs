namespace EvangelionERPV2.Shared.DTOs
{
    public class PayableBillDTO : BaseDTO
    {
        public string Description { get; set; } = string.Empty;
        public DateTime DueDate { get; set; } = DateTime.UtcNow;
        public DateTime? PaidAt { get; set; }
        public double Amount { get; set; }
        public bool IsPaid { get; set; }
        public Guid EnterpriseId { get; set; }
    }
}
