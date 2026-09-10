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

    /// <summary>
    /// One turn. Also the cost ledger: the token and latency columns are the only record of what a
    /// turn cost, and they stay meaningful if the provider is ever swapped for a paid one, at which
    /// point a money column is one migration rather than a redesign.
    /// </summary>
    /// <remarks>
    /// Content may hold customer data. It is never written to Serilog — this row is the only place
    /// it lives, and it is reachable only behind ai.chat.
    /// </remarks>
    [Index(nameof(AiConversationId), nameof(CreatedAt))]
    public class AiMessage : BaseEntity
    {
        public Guid AiConversationId { get; set; }

        [ForeignKey(nameof(AiConversationId))]
        public virtual AiConversation? AiConversation { get; set; }

        [MaxLength(16)]
        public string Role { get; set; } = AiMessageRole.User;

        public string Content { get; set; } = string.Empty;

        /// <summary>The tool calls this assistant turn asked for, as JSON. "[]" when it asked for none.</summary>
        public string ToolCallsJson { get; set; } = "[]";

        /// <summary>What those tools returned, as JSON. Kept so a turn can be replayed for an eval.</summary>
        public string ToolResultsJson { get; set; } = "[]";

        [MaxLength(128)]
        public string Model { get; set; } = string.Empty;

        public int PromptTokens { get; set; } = 0;
        public int CompletionTokens { get; set; } = 0;
        public long LatencyMs { get; set; } = 0;
    }
}
