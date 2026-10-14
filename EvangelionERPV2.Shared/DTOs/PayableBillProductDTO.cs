namespace EvangelionERPV2.Shared.DTOs
{
    public class PayableBillProductDTO : BaseDTO
    {
        public Guid PayableBillId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitValue { get; set; }
        public decimal LineAmount { get; set; }
        public string UnitOfMeasure { get; set; } = string.Empty;
    }
}
