using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    public static class AiPendingActionStatus
    {
        public const string Pending = "Pending";
        public const string Executing = "Executing";
        public const string Confirmed = "Confirmed";
        public const string Rejected = "Rejected";
        public const string Expired = "Expired";
        public const string Failed = "Failed";
    }

    /// <summary>
    /// A write the model proposed and a human has not yet allowed. It carries the arguments rather
    /// than the effect: nothing happens until <c>AiPendingActionService</c> replays it against the
    /// tool, so an action that sits here past <see cref="ExpiresAt"/> is reasoning about a state
    /// that may no longer exist and is refused instead of applied.
    /// </summary>
    [Index(nameof(AiConversationId), nameof(Status))]
    [Index(nameof(Status), nameof(ExpiresAt))]
    public class AiPendingAction : BaseEntity
    {
        public Guid AiConversationId { get; set; }

        [ForeignKey(nameof(AiConversationId))]
        public virtual AiConversation? AiConversation { get; set; }

        [MaxLength(64)]
        public string ToolName { get; set; } = string.Empty;

        public string ArgumentsJson { get; set; } = "{}";

        /// <summary>What the confirmation card shows the human, in pt-BR.</summary>
        [MaxLength(512)]
        public string Summary { get; set; } = string.Empty;

        [MaxLength(16)]
        public string Status { get; set; } = AiPendingActionStatus.Pending;

        public Guid? ConfirmedByUserId { get; set; }

        public DateTime? ExecutedAt { get; set; }

        public string ResultJson { get; set; } = "{}";

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow;
    }
}
