using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(UserId), nameof(ChangedAt))]
    [Index(nameof(EnterpriseId), nameof(ChangedAt))]
    [Index(nameof(EntityName), nameof(EntityId), nameof(ChangedAt))]
    public class AuditTrail
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [ForeignKey(nameof(User))]
        public Guid? UserId { get; set; }
        public virtual User? User { get; set; }

        public Guid? EnterpriseId { get; set; }

        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(16)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(128)]
        public string EntityName { get; set; } = string.Empty;

        public Guid EntityId { get; set; }

        public string ChangesJson { get; set; } = "{}";
    }
}
