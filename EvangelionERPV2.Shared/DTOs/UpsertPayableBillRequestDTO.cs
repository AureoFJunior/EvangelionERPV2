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
        public decimal? Amount { get; set; }
        /// <summary>Months between repeats. Null — the default — means the bill does not repeat.</summary>
        public int? RecurrenceMonths { get; set; }

        /// <summary>The last due date the chain may reach. Required when RecurrenceMonths is set.</summary>
        public DateTime? RecurrenceUntilUtc { get; set; }

        public IEnumerable<PayableBillItemRequestDTO>? Items { get; set; }

        /// <summary>On create only: the goods are already here, so the item lines go into stock with the bill.</summary>
        public bool ReceiveProducts { get; set; }
    }
}
