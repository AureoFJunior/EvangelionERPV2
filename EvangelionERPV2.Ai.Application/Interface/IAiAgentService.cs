namespace EvangelionERPV2.AiModule.Application.Interface
{
    public sealed record AiTurnRequest(
        Guid ConversationId,
        string UserMessage,
        IReadOnlySet<string> GrantedPermissions,
        AiToolContext Context,
        /// <summary>The answer is going to a speaker, so it is written to be heard.</summary>
        bool Spoken = false);

    public sealed record AiTurnResult(
        string Text,
        IReadOnlyList<string> ToolsCalled,
        int PromptTokens,
        int CompletionTokens,
        long LatencyMs,
        IReadOnlyList<AiPendingActionCard> PendingActions);

    public interface IAiAgentService
    {
        Task<AiTurnResult> RunTurnAsync(AiTurnRequest request, CancellationToken cancellation = default);
    }
}
