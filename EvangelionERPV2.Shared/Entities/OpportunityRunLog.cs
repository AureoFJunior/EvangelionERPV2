using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(RunId), IsUnique = true)]
    [Index(nameof(EnterpriseId), nameof(StartedAt))]
    [Table("OpportunityRunLog")]
    public class OpportunityRunLog : BaseEntity
    {
        public Guid RunId { get; set; }

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }

        [ForeignKey(nameof(User))]
        public Guid? RequestedByUserId { get; set; }
        public virtual User? User { get; set; }

        public string TriggerType { get; set; } = "manual";
        public string Status { get; set; } = "running";
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
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
