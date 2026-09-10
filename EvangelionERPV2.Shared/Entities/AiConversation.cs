using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(EnterpriseId), nameof(UserId), nameof(CreatedAt))]
    public class AiConversation : BaseEntity
    {
        public Guid EnterpriseId { get; set; }

        [ForeignKey(nameof(EnterpriseId))]
        public virtual Enterprise? Enterprise { get; set; }

        public Guid UserId { get; set; }

        [MaxLength(256)]
        public string Title { get; set; } = string.Empty;

        public virtual ICollection<AiMessage>? Messages { get; set; } = new List<AiMessage>();
    }
}
