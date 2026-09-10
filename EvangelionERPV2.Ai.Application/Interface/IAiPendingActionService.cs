namespace EvangelionERPV2.AiModule.Application.Interface
{
    /// <summary>What the client renders as a confirmation card.</summary>
    public sealed record AiPendingActionCard(
        Guid Id,
        string ToolName,
        string Summary,
        string ArgumentsJson,
        DateTime ExpiresAt);

    public sealed record AiPendingActionOutcome(bool Success, string Message, string ResultJson);

    public interface IAiPendingActionService
    {
        /// <summary>Records a write the model proposed. Nothing runs until someone confirms it.</summary>
        Task<AiPendingActionCard> ProposeAsync(
            Guid conversationId,
            IAiTool tool,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellation = default);

        Task<IReadOnlyList<AiPendingActionCard>> PendingAsync(Guid conversationId);

        /// <summary>
        /// Replays the stored arguments against the tool. The caller's permissions are re-checked
        /// here rather than trusted from the turn that proposed it: this is a separate request, and
        /// a role may have changed between the two.
        /// </summary>
        Task<AiPendingActionOutcome> ConfirmAsync(
            Guid actionId,
            IReadOnlySet<string> grantedPermissions,
            AiToolContext context,
            CancellationToken cancellation = default);

        Task<AiPendingActionOutcome> RejectAsync(Guid actionId, AiToolContext context);

        /// <summary>Marks past-TTL proposals Expired. Bookkeeping — confirm refuses them regardless.</summary>
        Task<int> SweepExpiredAsync(CancellationToken cancellation = default);
    }
}
