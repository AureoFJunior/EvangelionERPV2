namespace EvangelionERPV2.Shared.DTOs
{
    public class ReplenishmentSuggestionDTO
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal CurrentStock { get; set; }
        public decimal DailyConsumption { get; set; }
        public int LeadTimeDays { get; set; }
        public int MinCoverageDays { get; set; }
        public int MaxCoverageDays { get; set; }
        public decimal CoverageDays { get; set; }
        public decimal SuggestedQuantity { get; set; }
        public string Alert { get; set; } = "none";
        public string Criticality { get; set; } = "low";
    }
}
