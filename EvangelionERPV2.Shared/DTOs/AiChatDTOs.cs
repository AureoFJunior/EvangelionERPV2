using System.ComponentModel.DataAnnotations;

namespace EvangelionERPV2.Shared.DTOs
{
    public class AiChatRequestDTO
    {
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
