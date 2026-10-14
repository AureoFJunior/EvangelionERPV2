namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class PayableBillItemRequestDTO
    {
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitValue { get; set; }
    }
}
