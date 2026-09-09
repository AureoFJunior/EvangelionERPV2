using System.ComponentModel.DataAnnotations;

namespace EvangelionERPV2.Shared.DTOs
{
    public class AiChatRequestDTO
    {
        /// <summary>
        /// Null starts a new thread. The response always carries the id to continue with.
        /// A supplied id that does not resolve to a conversation owned by the caller (wrong user,
        /// wrong tenant, or simply unknown) also silently starts a new thread rather than erroring —
        /// this is deliberate, not a bug: it avoids confirming or denying that a given id exists to
        /// a caller who does not own it.
        /// </summary>
        public Guid? ConversationId { get; set; }

        [Required]
        [MaxLength(2000)]
        public string Message { get; set; } = string.Empty;
    }

    public class AiChatResponseDTO
    {
        public Guid ConversationId { get; set; }
        public string Text { get; set; } = string.Empty;
        public IReadOnlyList<string> ToolsCalled { get; set; } = Array.Empty<string>();
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public long LatencyMs { get; set; }
    }
}
