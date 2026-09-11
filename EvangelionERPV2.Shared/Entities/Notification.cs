using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using EvangelionERPV2.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// A message an admin wrote to everyone in the tenant.
    /// </summary>
    /// <remarks>
    /// Only manual broadcasts are stored. The automatic alerts that share the same bell are
    /// projected from <c>DailyPanelDTO</c> at read time and never land here: persisting a derived
    /// view of a bill creates a second copy that goes stale the moment the bill is settled in the
    /// bank's own system. A broadcast has no document behind it — it is the document — which is
    /// exactly what earns it a table.
    ///
    /// One row per broadcast, not one per recipient. Who has read it lives in
    /// <see cref="NotificationRead"/>, which gains a row only when somebody actually reads.
    /// </remarks>
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
