namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class OrderLineItemRequestDTO
    {
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Value { get; set; }
    }
}
