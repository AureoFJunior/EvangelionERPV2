using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.NotificationModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Serilog;

namespace EvangelionERPV2.NotificationModule.Application.Services
{
    public sealed class NotificationService : INotificationService
    {
        private readonly IRepository<Notification> _notifications;
        private readonly IRepository<NotificationRead> _reads;
        private readonly IDailyPanelService _panel;

        public NotificationService(
            IRepository<Notification> notifications,
            IRepository<NotificationRead> reads,
            IDailyPanelService panel)
        {
            _notifications = notifications;
            _reads = reads;
            _panel = panel;
        }

        public async Task<NotificationFeedDTO> GetFeedAsync(
            Guid enterpriseId, Guid userId, CancellationToken cancellation = default)
        {
            var now = DateTime.UtcNow;
            var broadcasts = await LiveBroadcastsAsync(enterpriseId, now);
            var readIds = await ReadIdsAsync(userId);

            var items = broadcasts
                .Select(broadcast => new NotificationItemDTO
                {
                    Id = broadcast.Id,
                    Source = NotificationSources.Broadcast,
                    Key = null,
                    Severity = broadcast.Severity,
                    Title = broadcast.Title,
                    Body = broadcast.Body,
                    CreatedAtUtc = broadcast.CreatedAt,
                    IsRead = readIds.Contains(broadcast.Id),
                    RequiredPermission = null,
                })
                .ToList();

            var (alerts, _) = await GetAlertsAsync(enterpriseId, cancellation);
            items.AddRange(alerts);

            return new NotificationFeedDTO
            {
                Items = items.OrderByDescending(item => item.CreatedAtUtc).ToList(),
                UnreadBroadcastCount = items.Count(item =>
                    item.Source == NotificationSources.Broadcast && !item.IsRead),
                ActiveAlertCount = alerts.Count,
                GeneratedAtUtc = now,
            };
        }

        public async Task<(IReadOnlyList<NotificationItemDTO> Alerts, string? Fingerprint)> GetAlertsAsync(
            Guid enterpriseId, CancellationToken cancellation = default)
        {
            try
            {
                var panel = await _panel.GetTodayAsync(enterpriseId, cancellation);
                var alerts = NotificationAlerts.From(panel, DateTime.UtcNow);

                return (alerts, NotificationAlerts.Fingerprint(alerts));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(
                    "Notification alerts unavailable. ErrorType={ErrorType}", ex.GetType().Name);

                return (Array.Empty<NotificationItemDTO>(), null);
            }
        }

        public async Task MarkReadAsync(
            Guid notificationId, Guid enterpriseId, Guid userId, CancellationToken cancellation = default)
        {
            var broadcast = await ByIdOrNullAsync(notificationId);

            if (broadcast is null || broadcast.EnterpriseId != enterpriseId || broadcast.IsActive != true)
                return;

            var already = await OrEmptyAsync(() => _reads.GetAllAsync(
                read => read.NotificationId == notificationId && read.UserId == userId));

            if (already.Count > 0)
                return;

            await _reads.CreateAsync(new NotificationRead
            {
                Id = Guid.NewGuid(),
                NotificationId = notificationId,
                UserId = userId,
                ReadAt = DateTime.UtcNow,
            });

            await _reads.CommitAsync(cancellation);
        }

        public async Task<int> MarkAllReadAsync(
            Guid enterpriseId, Guid userId, CancellationToken cancellation = default)
        {
            var broadcasts = await LiveBroadcastsAsync(enterpriseId, DateTime.UtcNow);
            var readIds = await ReadIdsAsync(userId);

            var unread = broadcasts
                .Where(broadcast => !readIds.Contains(broadcast.Id))
                .Select(broadcast => new NotificationRead
                {
                    Id = Guid.NewGuid(),
                    NotificationId = broadcast.Id,
                    UserId = userId,
                    ReadAt = DateTime.UtcNow,
                })
                .ToList();

            if (unread.Count == 0)
                return 0;

            await _reads.CreateRangeAsync(unread);
            await _reads.CommitAsync(cancellation);

            return unread.Count;
        }

        public async Task<Notification> BroadcastAsync(
            Guid enterpriseId, Guid authorUserId, CreateBroadcastDTO broadcast, CancellationToken cancellation = default)
        {
            ArgumentNullException.ThrowIfNull(broadcast);

            var created = await _notifications.CreateAsync(new Notification
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Title = broadcast.Title.Trim(),
                Body = broadcast.Body.Trim(),
                Severity = broadcast.Severity,
                CreatedByUserId = authorUserId,
                ExpiresAt = broadcast.ExpiresAt,
                IsActive = true,
            });

            await _notifications.CommitAsync(cancellation);

            return created;
        }

        public async Task<bool> RemoveBroadcastAsync(
            Guid notificationId, Guid enterpriseId, CancellationToken cancellation = default)
        {
            var broadcast = await ByIdOrNullAsync(notificationId);

            if (broadcast is null || broadcast.EnterpriseId != enterpriseId || broadcast.IsActive != true)
                return false;

            broadcast.IsActive = false;
            broadcast.UpdatedAt = DateTime.UtcNow;
            _notifications.Update(broadcast);
            await _notifications.CommitAsync(cancellation);

            return true;
        }

        /// <summary>Active, and not past its expiry. Expiry is optional; null never expires.</summary>
        private async Task<List<Notification>> LiveBroadcastsAsync(Guid enterpriseId, DateTime now) =>
            (await OrEmptyAsync(() => _notifications.GetAllAsync(
                notification => notification.EnterpriseId == enterpriseId
                                && notification.IsActive == true
                                && (notification.ExpiresAt == null || notification.ExpiresAt > now))))
            .ToList();

        private async Task<HashSet<Guid>> ReadIdsAsync(Guid userId) =>
            (await OrEmptyAsync(() => _reads.GetAllAsync(read => read.UserId == userId)))
            .Select(read => read.NotificationId)
            .ToHashSet();

        private Task<Notification?> ByIdOrNullAsync(Guid id) =>
            OrNullAsync(() => _notifications.GetByIdAsync(id));

        /// <summary>
        /// The repositories throw when a read finds nothing rather than answering empty, so every
        /// read here goes through one of these.
        /// </summary>
        private static async Task<IReadOnlyList<T>> OrEmptyAsync<T>(Func<Task<IEnumerable<T>>> query)
        {
            try
            {
                return (await query()).ToList();
            }
            catch (NotFoundDatabaseException)
            {
                return Array.Empty<T>();
            }
        }

        private static async Task<T?> OrNullAsync<T>(Func<Task<T>> query) where T : class
        {
            try
            {
                return await query();
            }
            catch (NotFoundDatabaseException)
            {
                return null;
            }
        }
    }
}
