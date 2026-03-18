namespace EvangelionERPV2.Shared.DTOs
{
    public class OpportunityDTO
    {
        public Guid Id { get; set; }
        public Guid EnterpriseId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SourceRule { get; set; } = string.Empty;
        public string SourceModel { get; set; } = string.Empty;
        public string Hypothesis { get; set; } = string.Empty;
        public string ExplainabilityJson { get; set; } = "{}";
        public Guid RunId { get; set; }
        public double ConfidenceScore { get; set; }
        public double EstimatedRevenueImpact { get; set; }
        public double EstimatedMarginImpact { get; set; }
        public double EstimatedCashImpact { get; set; }
        public double PriorityScore { get; set; }
        public DateTime DetectedAt { get; set; }
        public DateTime LastEvaluatedAt { get; set; }
        public OpportunityRecommendationDTO? Recommendation { get; set; }
        public IEnumerable<OpportunitySignalDTO> Signals { get; set; } = [];
        public IEnumerable<OpportunityFeedbackDTO> Feedbacks { get; set; } = [];
    }
}
