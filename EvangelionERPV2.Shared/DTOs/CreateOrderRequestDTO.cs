namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class CreateOrderRequestDTO
    {
        public Guid CustomerId { get; set; }
        public DateTime PaymentScheduledDate { get; set; }
        public int Status { get; set; }
        public IEnumerable<OrderLineItemRequestDTO> Items { get; set; } = [];
    }
}
