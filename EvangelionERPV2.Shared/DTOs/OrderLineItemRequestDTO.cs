namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class OrderLineItemRequestDTO
    {
        public Guid ProductId { get; set; }
        public double Quantity { get; set; }
        public double Value { get; set; }
    }
}
