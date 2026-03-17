namespace EvangelionERPV2.Shared.DTOs
{
    public class OpportunitySummaryDTO
    {
        public int Total { get; set; }
        public int NewCount { get; set; }
        public int InAnalysisCount { get; set; }
        public int AcceptedCount { get; set; }
        public int ExecutedCount { get; set; }
        public int ArchivedCount { get; set; }
        public double EstimatedRevenueImpact { get; set; }
        public double EstimatedMarginImpact { get; set; }
        public double EstimatedCashImpact { get; set; }
        public double AcceptanceRate { get; set; }
        public double ImplementationRate { get; set; }
        public double RealVsEstimatedUplift { get; set; }
        public double AverageProcessingTimeMs { get; set; }
        public Guid? LastRunId { get; set; }
        public DateTime? LastRunAt { get; set; }
        public Dictionary<string, int> OpportunitiesByType { get; set; } = [];
    }
}
