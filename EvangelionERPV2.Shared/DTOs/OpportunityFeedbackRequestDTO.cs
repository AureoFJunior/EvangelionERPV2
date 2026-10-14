namespace EvangelionERPV2.Shared.DTOs
{
    public class OpportunityFeedbackRequestDTO
    {
        public string Status { get; set; } = "ignored";
        public string Comment { get; set; } = string.Empty;
        public string MetadataJson { get; set; } = "{}";
        public decimal? RealRevenueImpact { get; set; }
        public decimal? RealMarginImpact { get; set; }
        public decimal? RealCashImpact { get; set; }
    }
}
