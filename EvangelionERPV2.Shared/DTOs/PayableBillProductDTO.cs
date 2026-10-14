namespace EvangelionERPV2.Shared.DTOs
{
    public class PayableBillProductDTO : BaseDTO
    {
        public Guid PayableBillId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public double Quantity { get; set; }
        public double UnitValue { get; set; }
        public double LineAmount { get; set; }
        public string UnitOfMeasure { get; set; } = string.Empty;
    }
}
