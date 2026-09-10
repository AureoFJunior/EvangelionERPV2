using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    public static class AiMessageRole
    {
        public const string System = "system";
        public const string User = "user";
        public const string Assistant = "assistant";
        public const string Tool = "tool";
    }

    [Index(nameof(AiConversationId), nameof(CreatedAt))]
    public class AiMessage : BaseEntity
    {
        public Guid AiConversationId { get; set; }

        [ForeignKey(nameof(AiConversationId))]
        public virtual AiConversation? AiConversation { get; set; }

        [MaxLength(16)]
        public string Role { get; set; } = AiMessageRole.User;

        public string Content { get; set; } = string.Empty;

        public string ToolCallsJson { get; set; } = "[]";

        public string ToolResultsJson { get; set; } = "[]";

        [MaxLength(128)]
        public string Model { get; set; } = string.Empty;

        public int PromptTokens { get; set; } = 0;
        public int CompletionTokens { get; set; } = 0;
        public long LatencyMs { get; set; } = 0;
    }
}
