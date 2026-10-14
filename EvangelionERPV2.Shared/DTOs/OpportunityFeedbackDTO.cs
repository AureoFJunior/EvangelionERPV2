namespace EvangelionERPV2.Shared.DTOs
{
    public class OpportunityFeedbackDTO
    {
        public Guid Id { get; set; }
        public Guid OpportunityId { get; set; }
        public Guid? UserId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string MetadataJson { get; set; } = "{}";
        public double? RealRevenueImpact { get; set; }
        public double? RealMarginImpact { get; set; }
        public double? RealCashImpact { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
