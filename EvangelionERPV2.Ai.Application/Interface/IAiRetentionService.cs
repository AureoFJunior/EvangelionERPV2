namespace EvangelionERPV2.AiModule.Application.Interface
{
    public interface IAiRetentionService
    {
        /// <summary>
        /// Deletes conversations past the retention window, with their messages and cards.
        /// </summary>
        /// <returns>How many conversations were removed.</returns>
        Task<int> PurgeAsync(CancellationToken cancellation = default);
    }
}
