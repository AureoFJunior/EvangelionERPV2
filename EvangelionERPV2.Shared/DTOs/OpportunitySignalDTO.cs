namespace EvangelionERPV2.Shared.DTOs
{
    public class OpportunitySignalDTO
    {
        public Guid Id { get; set; }
        public string SignalType { get; set; } = string.Empty;
        public string SignalKey { get; set; } = string.Empty;
        public double SignalValue { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string MetadataJson { get; set; } = "{}";
        public string SourceEntity { get; set; } = string.Empty;
        public string SourceEntityId { get; set; } = string.Empty;
        public DateTime CapturedAt { get; set; }
    }
}
