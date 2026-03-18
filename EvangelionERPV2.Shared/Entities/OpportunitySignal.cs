using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(OpportunityId), nameof(SignalType))]
    [Index(nameof(SourceEntity), nameof(SourceEntityId))]
    [Table("OpportunitySignal")]
    public class OpportunitySignal : BaseEntity
    {
        [ForeignKey(nameof(Opportunity))]
        public Guid OpportunityId { get; set; }

        [JsonIgnore]
        public virtual Opportunity? Opportunity { get; set; }

        public string SignalType { get; set; } = string.Empty;
        public string SignalKey { get; set; } = string.Empty;
        public double SignalValue { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string MetadataJson { get; set; } = "{}";
        public string SourceEntity { get; set; } = string.Empty;
        public string SourceEntityId { get; set; } = string.Empty;
        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    }
}
