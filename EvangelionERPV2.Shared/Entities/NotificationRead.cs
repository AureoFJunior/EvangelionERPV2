using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One user has read one broadcast. Absence of a row is the unread state.
    /// </summary>
    /// <remarks>
    /// Deliberately has no EnterpriseId, so <c>ApplyTenantQueryFilters</c> leaves it alone: the row
    /// is scoped through its <see cref="Notification"/>, and a second filter on the join side would
    /// be a second copy of a truth the parent already enforces — one that silently disagrees the
    /// day a broadcast is moved between tenants.
    /// </remarks>
    [Index(nameof(NotificationId), nameof(UserId), IsUnique = true)]
    [Index(nameof(UserId))]
    public class NotificationRead : BaseEntity
    {
        [ForeignKey(nameof(Notification))]
        public Guid NotificationId { get; set; }
        public Notification? Notification { get; set; }

        [ForeignKey(nameof(User))]
        public Guid UserId { get; set; }
        public User? User { get; set; }

        public DateTime ReadAt { get; set; } = DateTime.UtcNow;
    }
}
