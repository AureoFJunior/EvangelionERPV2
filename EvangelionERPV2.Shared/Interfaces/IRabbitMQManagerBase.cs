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

        /// <summary>
        /// Registers one consumer and processes messages until the token is cancelled. Prefer this
        /// to looping over <see cref="DequeueAndProcessAsync{T}(Func{T,Task},CancellationToken)"/>,
        /// which serves a single message per call and caps a queue at well under one message a
        /// second. Handlers may run concurrently.
        /// </summary>
        Task ConsumeAsync<T>(Func<T, Task> processMessageAsync, CancellationToken cancellationToken);
        Task EnqueueAsync<T>(T obj);
        void SetupChannel();
    }
}
