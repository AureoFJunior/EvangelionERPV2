using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(OpportunityId), nameof(Status), nameof(CreatedAt))]
    [Index(nameof(UserId), nameof(CreatedAt))]
    [Table("OpportunityFeedback")]
    public class OpportunityFeedback : BaseEntity
    {
        [ForeignKey(nameof(Opportunity))]
        public Guid OpportunityId { get; set; }

        [JsonIgnore]
        public virtual Opportunity? Opportunity { get; set; }

        [ForeignKey(nameof(User))]
        public Guid? UserId { get; set; }

        [JsonIgnore]
        public virtual User? User { get; set; }

        public string Status { get; set; } = "ignored";
        public string Comment { get; set; } = string.Empty;
        public string MetadataJson { get; set; } = "{}";
        public double? RealRevenueImpact { get; set; }
        public double? RealMarginImpact { get; set; }
        public double? RealCashImpact { get; set; }
    }
}
