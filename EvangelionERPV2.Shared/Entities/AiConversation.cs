using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One chat thread. Carries EnterpriseId so ApplyTenantQueryFilters picks it up by convention:
    /// a conversation is exactly as tenant-scoped as the data it discusses.
    /// </summary>
    [Index(nameof(EnterpriseId), nameof(UserId), nameof(CreatedAt))]
    public class AiConversation : BaseEntity
    {
        public Guid EnterpriseId { get; set; }

        [ForeignKey(nameof(EnterpriseId))]
        public virtual Enterprise? Enterprise { get; set; }

        /// <summary>Who is talking. Two people in one tenant do not share a thread.</summary>
        public Guid UserId { get; set; }

        [MaxLength(256)]
        public string Title { get; set; } = string.Empty;

        public virtual ICollection<AiMessage>? Messages { get; set; } = new List<AiMessage>();
    }
}
