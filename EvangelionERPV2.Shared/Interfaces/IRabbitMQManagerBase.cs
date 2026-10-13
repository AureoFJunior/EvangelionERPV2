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
        /// <summary>
        /// Registers a long-lived consumer. The handler receives the deserialized body and the
        /// broker's <c>MessageId</c>, which is preserved across retries and redrives and is
        /// therefore the key a handler uses to recognise work it has already done.
        /// </summary>
        Task ConsumeAsync<T>(Func<T, string, Task> processMessageAsync, CancellationToken cancellationToken);
        /// <summary>
        /// Moves up to <paramref name="maxMessages"/> messages from the dead-letter queue back onto
        /// the work queue with their retry count reset. Operator-triggered and bounded on purpose -
        /// a redrive is a bulk retry, and re-processing is only safe if the handler is idempotent.
        /// </summary>
        Task<int> RedriveDeadLetteredAsync(int maxMessages, CancellationToken cancellationToken = default);

        Task EnqueueAsync<T>(T obj);
    }
}
