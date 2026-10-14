namespace EvangelionERPV2.Shared.DTOs
{
    public class ReplenishmentSuggestionDTO
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public double CurrentStock { get; set; }
        public double DailyConsumption { get; set; }
        public int LeadTimeDays { get; set; }
        public int MinCoverageDays { get; set; }
        public int MaxCoverageDays { get; set; }
        public double CoverageDays { get; set; }
        public double SuggestedQuantity { get; set; }
        public string Alert { get; set; } = "none";
        public string Criticality { get; set; } = "low";
    }
}
