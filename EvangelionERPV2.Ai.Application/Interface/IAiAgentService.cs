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
        long LatencyMs,
        IReadOnlyList<AiPendingActionCard> PendingActions);

    public interface IAiAgentService
    {
        /// <remarks>
        /// The caller must already have resolved the conversation and confirmed it belongs to the
        /// calling user and tenant; this method does not re-check that.
        /// </remarks>
        Task<AiTurnResult> RunTurnAsync(AiTurnRequest request, CancellationToken cancellation = default);
    }
}
