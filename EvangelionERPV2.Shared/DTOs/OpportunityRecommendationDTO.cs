namespace EvangelionERPV2.Shared.DTOs
{
    public class OpportunityRecommendationDTO
    {
        public Guid Id { get; set; }
        public string ActionTitle { get; set; } = string.Empty;
        public string ActionDescription { get; set; } = string.Empty;
        public string WhyRecommended { get; set; } = string.Empty;
        public string ActionPayloadJson { get; set; } = "{}";
        public string PriorityLabel { get; set; } = "medium";
    }
}
