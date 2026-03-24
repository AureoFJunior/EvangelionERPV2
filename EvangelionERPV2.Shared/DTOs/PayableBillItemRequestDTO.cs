namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class PayableBillItemRequestDTO
    {
        public Guid ProductId { get; set; }
        public double Quantity { get; set; }
        public double UnitValue { get; set; }
    }
}
