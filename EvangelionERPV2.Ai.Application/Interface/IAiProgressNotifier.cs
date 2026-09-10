namespace EvangelionERPV2.AiModule.Application.Interface
{
    public interface IAiProgressNotifier
    {
        Task StatusAsync(Guid userId, string status);
        Task ChunkAsync(Guid userId, string text);
        Task DoneAsync(Guid userId, Guid conversationId);
    }
}
