using EvangelionERPV2.Shared.Entities.RabbitMQ;

namespace EvangelionERPV2.Shared.Interfaces
{
    public interface IRabbitMQManagerBase
    {
        /// <summary>
        /// Whether the broker connection is currently up. The connection is opened in the
        /// constructor and recovered automatically, so this is the cheapest honest answer to
        /// "can this host publish right now" - no probe message, no queue side effects.
        /// </summary>
        bool IsConnected { get; }

        Task<T> DequeueAndProcessAsync<T>();
        Task DequeueAndProcessAsync<T>(Func<T, Task> processMessageAsync, CancellationToken cancellationToken = default);
        Task EnqueueAsync<T>(T obj);
        void SetupChannel();
    }
}
