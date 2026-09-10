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
        /// <remarks>
        /// Precondition: the caller has already resolved the conversation and confirmed it belongs
        /// to the calling user and tenant. This method does not re-check that. The controller does
        /// it once, against a DbContext whose global filter already scopes the read to the tenant;
        /// repeating it in the service would be a second source of truth for the same rule.
        /// </remarks>
        Task<AiTurnResult> RunTurnAsync(AiTurnRequest request, CancellationToken cancellation = default);
    }
}
