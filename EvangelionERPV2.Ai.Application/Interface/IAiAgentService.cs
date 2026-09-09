namespace EvangelionERPV2.AiModule.Application.Interface
{
    public sealed record AiTurnRequest(
        Guid ConversationId,
        string UserMessage,
        IReadOnlySet<string> GrantedPermissions,
        AiToolContext Context);

    public sealed record AiTurnResult(
        string Text,
        IReadOnlyList<string> ToolsCalled,
        int PromptTokens,
        int CompletionTokens,
        long LatencyMs);

    public interface IAiAgentService
    {
        Task<AiTurnResult> RunTurnAsync(AiTurnRequest request, CancellationToken cancellation = default);
    }
}
