namespace EvangelionERPV2.AiModule.Application.Interface
{
    /// <summary>
    /// Implemented in Web, because the hub lives there and this module must not depend on SignalR.
    /// A no-op implementation is a valid one — progress is decoration, and a failed notification
    /// must never fail the turn.
    /// </summary>
    public interface IAiProgressNotifier
    {
        Task StatusAsync(Guid userId, string status);
        Task ChunkAsync(Guid userId, string text);
        Task DoneAsync(Guid userId, Guid conversationId);
    }
}
