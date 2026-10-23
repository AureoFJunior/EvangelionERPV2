using System.Text.Json.Serialization;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class MarkAllReadResponseDto
    {
        public int Marked { get; set; }
    }

    /// <summary>
    /// Everything the bell renders, in one call: the broadcasts an admin wrote and the alerts the
    /// daily panel is currently firing.
    /// </summary>
    public class NotificationFeedDTO
    {
        public List<NotificationItemDTO> Items { get; set; } = [];

        /// <summary>
        /// Drives the badge, and reaches zero when the user has read everything.
        /// </summary>
        public int UnreadBroadcastCount { get; set; }

        /// <summary>
        /// How many alerts are firing. Deliberately not folded into the badge: an alert is a state,
        /// not a message, so a badge including it could never reach zero while the company owes
        /// anyone money — which teaches people to ignore the badge.
        /// </summary>
        public int ActiveAlertCount { get; set; }

        public DateTime GeneratedAtUtc { get; set; }
    }

    public class NotificationItemDTO
    {
        /// <summary>Set for a broadcast, null for an alert — an alert has no row.</summary>
        public Guid? Id { get; set; }

        /// <summary><see cref="NotificationSources.Broadcast"/> or <see cref="NotificationSources.Alert"/>.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>Stable key for an alert ("payables.overdue"), null for a broadcast.</summary>
        public string? Key { get; set; }

        public EnumNotificationSeverity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }

        /// <summary>Always false for an alert: there is nothing to mark read, only a document to fix.</summary>
        public bool IsRead { get; set; }

        /// <summary>
        /// The permission a caller needs before this item may be shown to them, or null for
        /// everyone in the tenant. Never serialised: the controller filters on it and the client
        /// has no use for it, so putting it on the wire would only publish the gate.
        /// </summary>
        [JsonIgnore]
        public string? RequiredPermission { get; set; }
    }

    public static class NotificationSources
    {
        public const string Broadcast = "broadcast";
        public const string Alert = "alert";
    }

    public class CreateBroadcastDTO
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public EnumNotificationSeverity Severity { get; set; } = EnumNotificationSeverity.Info;
        public DateTime? ExpiresAt { get; set; }
    }

    /// <summary>What a sweep pass did, so a worker log line says something.</summary>
    public class NotificationSweepResultDTO
    {
        public int EnterprisesScanned { get; set; }
        public int EnterprisesNotified { get; set; }
        public int EnterprisesFailed { get; set; }
    }
}
