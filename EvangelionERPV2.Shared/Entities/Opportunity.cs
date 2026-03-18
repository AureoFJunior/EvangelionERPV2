using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(EnterpriseId), nameof(Type), nameof(Status))]
    [Index(nameof(EnterpriseId), nameof(Fingerprint), IsUnique = true)]
    [Index(nameof(RunId), nameof(Type), nameof(IsActive))]
    [Table("Opportunity")]
    public class Opportunity : BaseEntity
    {
        [ForeignKey(nameof(Enterprise))]
        public Guid EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }

        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = "new";
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SourceRule { get; set; } = string.Empty;
        public string SourceModel { get; set; } = string.Empty;
        public string Hypothesis { get; set; } = string.Empty;
        public string ExplainabilityJson { get; set; } = "{}";
        public string Fingerprint { get; set; } = string.Empty;

        public Guid RunId { get; set; }
        public double ConfidenceScore { get; set; }
        public double EstimatedRevenueImpact { get; set; }
        public double EstimatedMarginImpact { get; set; }
        public double EstimatedCashImpact { get; set; }
        public double PriorityScore { get; set; }
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastEvaluatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<OpportunitySignal>? Signals { get; set; } = new List<OpportunitySignal>();
        public virtual ICollection<OpportunityRecommendation>? Recommendations { get; set; } = new List<OpportunityRecommendation>();
        public virtual ICollection<OpportunityFeedback>? Feedbacks { get; set; } = new List<OpportunityFeedback>();
    }
}
