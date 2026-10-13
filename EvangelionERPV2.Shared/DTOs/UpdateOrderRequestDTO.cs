namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UpdateOrderRequestDTO
    {
        public Guid Id { get; set; }
        public Guid? CustomerId { get; set; }
        public DateTime? PaymentScheduledDate { get; set; }
        public DateTime? Payday { get; set; }
        public int Status { get; set; }

        /// <summary>Null keeps what is stored; blank clears it.</summary>
        public string? TrackingCode { get; set; }
        public string? TrackingUrl { get; set; }
    }
}
