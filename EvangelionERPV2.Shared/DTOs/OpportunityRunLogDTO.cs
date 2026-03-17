namespace EvangelionERPV2.Shared.DTOs
{
    public class OpportunityRunLogDTO
    {
        public Guid Id { get; set; }
        public Guid RunId { get; set; }
        public Guid? EnterpriseId { get; set; }
        public Guid? RequestedByUserId { get; set; }
        public string TriggerType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public int TotalGenerated { get; set; }
        public int TotalUpdated { get; set; }
        public int TotalArchived { get; set; }
        public int DurationMs { get; set; }
        public string DetectorStatsJson { get; set; } = "{}";
        public string ErrorMessage { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
    }
}
