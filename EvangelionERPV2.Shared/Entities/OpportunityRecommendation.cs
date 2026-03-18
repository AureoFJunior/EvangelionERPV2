using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(OpportunityId), nameof(CreatedAt))]
    [Table("OpportunityRecommendation")]
    public class OpportunityRecommendation : BaseEntity
    {
        [ForeignKey(nameof(Opportunity))]
        public Guid OpportunityId { get; set; }

        [JsonIgnore]
        public virtual Opportunity? Opportunity { get; set; }

        public string ActionTitle { get; set; } = string.Empty;
        public string ActionDescription { get; set; } = string.Empty;
        public string WhyRecommended { get; set; } = string.Empty;
        public string ActionPayloadJson { get; set; } = "{}";
        public string PriorityLabel { get; set; } = "medium";
    }
}
