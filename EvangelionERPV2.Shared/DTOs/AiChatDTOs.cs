using System.ComponentModel.DataAnnotations;

namespace EvangelionERPV2.Shared.DTOs
{
    public class AiChatRequestDTO
    {
        public Guid? ConversationId { get; set; }

        [Required]
        [MaxLength(2000)]
        public string Message { get; set; } = string.Empty;

        /// <summary>Set by voice mode: the answer will be spoken, so it is written to be heard.</summary>
        public bool Spoken { get; set; }
    }

    public class AiChatResponseDTO
    {
        public Guid ConversationId { get; set; }
        public string Text { get; set; } = string.Empty;
        public IReadOnlyList<string> ToolsCalled { get; set; } = Array.Empty<string>();
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public long LatencyMs { get; set; }

        /// <summary>Writes the model proposed. Nothing has run: each needs a confirm call.</summary>
        public IReadOnlyList<AiPendingActionDTO> PendingActions { get; set; } = Array.Empty<AiPendingActionDTO>();
    }

    /// <summary>What was said. There is no audio in this type, and none is kept anywhere.</summary>
    public class AiTranscriptDTO
    {
        public string Text { get; set; } = string.Empty;

        public long LatencyMs { get; set; }
    }

    public class AiPendingActionDTO
    {
        public Guid Id { get; set; }

        public string ToolName { get; set; } = string.Empty;

        /// <summary>The pt-BR line the confirmation card shows.</summary>
        public string Summary { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }
    }

    public class AiActionOutcomeDTO
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    public class AiConversationSummaryDTO
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        /// <summary>First words of the opening question, so a history list reads as something.</summary>
        public string Preview { get; set; } = string.Empty;
    }

    public class AiMessageDTO
    {
        public Guid Id { get; set; }

        public string Role { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public IReadOnlyList<string> ToolsCalled { get; set; } = Array.Empty<string>();
    }
}
