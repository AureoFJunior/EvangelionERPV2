namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UpsertPayableBillRequestDTO
    {
        public Guid? Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public int BillType { get; set; }
        public DateTime DueDate { get; set; }
        public bool IsPaid { get; set; }
        public DateTime? PaidAt { get; set; }
        public double? Amount { get; set; }
        public IEnumerable<PayableBillItemRequestDTO>? Items { get; set; }
    }
}
