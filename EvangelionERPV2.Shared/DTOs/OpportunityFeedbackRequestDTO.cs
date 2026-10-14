namespace EvangelionERPV2.Shared.DTOs
{
    public class OpportunityFeedbackRequestDTO
    {
        public string Status { get; set; } = "ignored";
        public string Comment { get; set; } = string.Empty;
        public string MetadataJson { get; set; } = "{}";
        public double? RealRevenueImpact { get; set; }
        public double? RealMarginImpact { get; set; }
        public double? RealCashImpact { get; set; }
    }
}
