using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using EvangelionERPV2.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// A message an admin wrote to everyone in the tenant.
    /// </summary>
    [Index(nameof(EnterpriseId), nameof(IsActive), nameof(CreatedAt))]
    public class Notification : BaseEntity
    {
        [ForeignKey(nameof(Enterprise))]
        public Guid EnterpriseId { get; set; }
        public Enterprise? Enterprise { get; set; }

        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Body { get; set; } = string.Empty;

        public EnumNotificationSeverity Severity { get; set; } = EnumNotificationSeverity.Info;

        [ForeignKey(nameof(CreatedByUser))]
        public Guid CreatedByUserId { get; set; }
        public User? CreatedByUser { get; set; }

        /// <summary>Null means it stays in the feed until an admin deletes it.</summary>
        public DateTime? ExpiresAt { get; set; }
    }
}
