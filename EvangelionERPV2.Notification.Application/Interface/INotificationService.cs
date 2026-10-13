using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NotificationModule.Application.Interface
{
    public interface INotificationService
    {
        /// <summary>
        /// Everything the bell renders for one user: the tenant's live broadcasts plus the alerts
        /// the daily panel is firing right now.
        /// </summary>
        Task<NotificationFeedDTO> GetFeedAsync(Guid enterpriseId, Guid userId, CancellationToken cancellation = default);

        /// <summary>Idempotent: reading twice is one row, not two.</summary>
        Task MarkReadAsync(Guid notificationId, Guid enterpriseId, Guid userId, CancellationToken cancellation = default);

        /// <summary>One read row per unread broadcast, in one commit.</summary>
        Task<int> MarkAllReadAsync(Guid enterpriseId, Guid userId, CancellationToken cancellation = default);

        Task<Notification> BroadcastAsync(
            Guid enterpriseId, Guid authorUserId, CreateBroadcastDTO broadcast, CancellationToken cancellation = default);

        /// <summary>Soft delete, matching every other delete in the solution.</summary>
        Task<bool> RemoveBroadcastAsync(Guid notificationId, Guid enterpriseId, CancellationToken cancellation = default);

        /// <summary>
        /// The alerts firing for one tenant, and their fingerprint. Used by the sweep to decide
        /// whether anything changed since last time.
        /// </summary>
        /// <returns>
        /// A null fingerprint means the panel could not be computed — which is not the same fact as
        /// "no alerts are firing", and the sweep must not read it as the alerts having cleared.
        /// </returns>
        Task<(IReadOnlyList<NotificationItemDTO> Alerts, string? Fingerprint)> GetAlertsAsync(
            Guid enterpriseId, CancellationToken cancellation = default);
    }
}
