using System.Collections.Concurrent;
using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System.Text;
using System.Text.Json;
using Serilog;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using EvangelionERPV2.Shared.Interfaces;
using EvangelionERPV2.Shared.Entities.RabbitMQ;
using Prometheus;
using EvangelionERPV2.Shared.Exceptions;

namespace EvangelionERPV2.Shared.Utils
{
    public abstract class RabbitMQManagerBase<TChannelSettings> : IRabbitMQManagerBase
    where TChannelSettings : class, IBaseChannelSettings
    {
        private const int MaxProcessingRetries = 3;

        /// <summary>
        /// Unacknowledged messages the broker may keep in flight per consumer. 20 is a working
        /// figure, not a tuned one: large enough that the consumer is never idle waiting for a
        /// delivery, small enough that a consumer dying does not strand a large batch for the
        /// redelivery timeout. Tune it against evangelion_worker_message_duration_seconds.
        /// </summary>
        private const ushort PrefetchCount = 20;

        /// <summary>
        /// Every worker's consume path comes through this class, so counting here covers all of
        /// them at once instead of once per worker. The failure it exists for is the quiet one: a
        /// consumer that is up, healthy and processing nothing. Liveness cannot see that - only a
        /// rate that has gone to zero can.
        /// <para>
        /// Labelled by queue, which is bounded and already named in configuration. Never by message
        /// id or payload: that is how a metrics database dies of cardinality.
        /// </para>
        /// </summary>
        private static readonly Counter MessagesProcessed = Metrics.CreateCounter(
            "evangelion_worker_messages_total",
            "Messages consumed from a queue, by outcome",
            new CounterConfiguration { LabelNames = ["queue", "result"] });

        /// <summary>
        /// Depth of the work queue and of its dead-letter queue. These are the only signals that can
        /// tell an idle worker from a stalled one, and the only way the dead-letter queue stops being
        /// invisible - nothing consumes it, so without a gauge a message that lands there is gone
        /// with no trace anywhere.
        /// <para>
        /// Read with a passive declare, which does not create anything. Note the ceiling: a passive
        /// declare against a queue that does not exist closes the channel, so this is only safe
        /// because <see cref="SetupChannel"/> declared both on this same channel at startup. If the
        /// queues are ever deleted underneath a running worker, this is not what breaks first.
        /// </para>
        /// </summary>
        private static readonly Gauge QueueDepth = Metrics.CreateGauge(
            "evangelion_queue_depth",
            "Messages waiting in the work queue",
            new GaugeConfiguration { LabelNames = ["queue"] });

        /// <summary>
        /// Messages moved back out of the dead-letter queue by an operator. Counted separately from
        /// ordinary throughput because a redrive is a deliberate act with a person behind it, and
        /// "how much have we replayed" is a different question from "how much are we processing".
        /// </summary>
        private static readonly Counter MessagesRedriven = Metrics.CreateCounter(
            "evangelion_dlq_redriven_total",
            "Messages republished from the dead-letter queue back onto the work queue",
            new CounterConfiguration { LabelNames = ["queue"] });

        private static readonly Gauge DlqDepth = Metrics.CreateGauge(
            "evangelion_dlq_depth",
            "Messages sitting in the dead-letter queue, which nothing consumes",
            new GaugeConfiguration { LabelNames = ["queue"] });

        /// <summary>
        /// Messages the broker accepted but could not route to any queue, handed back because every
        /// publish sets <c>mandatory: true</c>. Previously these were returned and discarded with
        /// nothing listening, so a misrouted publish looked identical to a successful one.
        /// </summary>
        private static readonly Counter UnroutableMessages = Metrics.CreateCounter(
            "evangelion_publish_unroutable_total",
            "Messages returned by the broker as unroutable",
            new CounterConfiguration { LabelNames = ["queue"] });

        private static readonly Histogram MessageDuration = Metrics.CreateHistogram(
            "evangelion_worker_message_duration_seconds",
            "Time spent in the message handler",
            new HistogramConfiguration
            {
                LabelNames = ["queue"],
                // A worker message is not an HTTP request: the Order worker calls back into the API
                // and the Email worker renders a PDF, so the interesting range runs to minutes, not
                // to the 32s the default HTTP buckets top out at.
                Buckets = Histogram.ExponentialBuckets(0.05, 2, 12)
            });

        private readonly IConnection _connection;

        /// <summary>
        /// The channel that consumes, declares and acknowledges. Never used to publish from a
        /// request thread — see <see cref="RentPublishChannelAsync"/>.
        /// </summary>
        private readonly IChannel _channel;

        /// <summary>
        /// Serialises everything that touches <see cref="_channel"/>. An <c>IChannel</c> is not
        /// thread-safe, and <c>ConsumerDispatchConcurrency</c> is 10, so up to ten handlers can be
        /// acknowledging at once.
        /// </summary>
        private readonly SemaphoreSlim _channelLock = new(1, 1);

        /// <summary>
        /// Publishing gets its own channels rather than sharing <see cref="_channel"/> under the
        /// lock: with publisher confirms a publish waits for the broker's ack, so serialising them
        /// would cap throughput at one round trip each.
        /// </summary>
        private readonly ConcurrentBag<IChannel> _publishChannels = new();

        private const int MaxPooledPublishChannels = 16;

        /// <summary>
        /// Five attempts over roughly 15 seconds, which rides out a broker restart without holding
        /// a publishing request open past its timeout.
        /// </summary>
        private const int MaxConnectAttempts = 5;

        private static readonly TimeSpan InitialConnectRetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxConnectRetryDelay = TimeSpan.FromSeconds(8);
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly TChannelSettings _channelSettings;
        private readonly AWSKMSKeyProvider _kmsProvider;

        public RabbitMQManagerBase(IOptions<RabbitMQSettings> rabbitMQSettings,
                             IOptions<TChannelSettings> channelSettings,
                             AWSKMSKeyProvider kmsProvider)
        {
            _kmsProvider = kmsProvider;
            _channelSettings = channelSettings.Value;
            var factory = new ConnectionFactory
            {
                HostName = _kmsProvider.GetKMSKey(rabbitMQSettings.Value.HostName),
                UserName = _kmsProvider.GetKMSKey(rabbitMQSettings.Value.UserName),
                Password = _kmsProvider.GetKMSKey(rabbitMQSettings.Value.Password),
                VirtualHost = _kmsProvider.GetKMSKey(rabbitMQSettings.Value.VirtualHost),
                Port = ResolveRabbitMqPort(_kmsProvider.GetKMSKey(rabbitMQSettings.Value.Port.ToString())),
                AutomaticRecoveryEnabled = true,
                ConsumerDispatchConcurrency = 10,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };
            ApplyRabbitMqUriIfConfigured(factory, _kmsProvider.GetKMSKey(rabbitMQSettings.Value.Uri));

            // After the URI has been applied, not before: this has to see the connection that will
            // actually be made, whichever way it was configured.
            EnsureTransportIsSecure(factory.Ssl?.Enabled ?? false, CurrentEnvironmentName());

            _connection = ConnectWithRetry(factory);

            // Publisher confirms. Without them BasicPublishAsync returns as soon as the bytes are
            // written to the socket, so InsertOrderInQueue logged success and the API answered 200
            // for messages the broker may never have stored - a lost order with no error anywhere.
            // With tracking on, the publish awaits the broker's ack and throws on a nack, and both
            // EnqueueAsync and InsertOrderInQueue already rethrow, so the failure now reaches the
            // caller instead of being invented away.
            //
            // The cost is real and intended: publishing is slower because it now waits for the
            // broker to answer. That is the price of knowing the message exists.
            _channel = _connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true,
                    outstandingPublisherConfirmationsRateLimiter: null,
                    consumerDispatchConcurrency: null))
                .GetAwaiter().GetResult();

            // Every publish here sets mandatory: true, which asks the broker to hand back a message
            // it cannot route. Nothing was listening, so those came back and were dropped in
            // silence. A return is not an exception - the broker accepted the message and then
            // found nowhere to put it - so it is counted and logged rather than thrown.
            _channel.BasicReturnAsync += (_, returned) =>
            {
                UnroutableMessages.WithLabels(_channelSettings.QueueName).Inc();
                Log.Logger.Error(
                    "Broker returned an unroutable message. Exchange={Exchange} RoutingKey={RoutingKey} Reply={ReplyCode} {ReplyText}",
                    returned.Exchange,
                    returned.RoutingKey,
                    returned.ReplyCode,
                    returned.ReplyText);
                return Task.CompletedTask;
            };

            SetupChannelCore();

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };
            
        }

        /// <inheritdoc />
        public bool IsConnected => _connection.IsOpen;

        private static int ResolveRabbitMqPort(string? rawPort)
        {
            var port = SharedFunctions.SafeConvertToNumber<int>(rawPort ?? string.Empty);
            return port > 0 ? port : 5672;
        }

        /// <summary>
        /// Refuses a cleartext broker connection outside Development.
        /// <para>
        /// Checks the factory's resolved TLS state rather than the URI scheme, because the scheme is
        /// only one of two ways in. An earlier version of this guard inspected the scheme string and
        /// was therefore dead whenever no URI was configured at all: the factory then falls back to
        /// HostName/Port, <see cref="ResolveRabbitMqPort"/> defaults to 5672, and the connection went
        /// out in cleartext with the check never running. Verified against RabbitMQ.Client 7.2.1 -
        /// an amqps URI sets Ssl.Enabled true and port 5671, amqp and the host/port path leave it
        /// false - so Ssl.Enabled is the one signal that cannot be bypassed by configuration shape.
        /// </para>
        /// <para>
        /// Failing closed means a misconfiguration stops the process instead of quietly publishing
        /// customer data in the open. An outage is visible and fixed in minutes; cleartext PII on
        /// the wire is neither.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">TLS is off outside Development.</exception>
        public static void EnsureTransportIsSecure(bool tlsEnabled, string? environmentName)
        {
            if (tlsEnabled)
                return;

            // Development only, and only when it says so explicitly. An unset environment is
            // treated as production: the safe reading of "I do not know where I am".
            if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
            {
                Log.Logger.Warning(
                    "RabbitMQ is connecting without TLS. Allowed only because the environment is Development.");
                return;
            }

            throw new InvalidOperationException(
                "RabbitMQ is configured without TLS outside Development. Use an amqps:// URI (port 5671): " +
                "the broker login and every queued message would otherwise cross the network in cleartext.");
        }

        private static string? CurrentEnvironmentName()
            => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
               ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

        private static void ApplyRabbitMqUriIfConfigured(ConnectionFactory factory, string? rawUri)
        {
            if (string.IsNullOrWhiteSpace(rawUri))
                return;

            var normalizedUri = rawUri.Trim();
            if (Uri.TryCreate(normalizedUri, UriKind.Absolute, out var rabbitMqUri) &&
                (rabbitMqUri.Scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase) ||
                 rabbitMqUri.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase)))
            {
                factory.Uri = rabbitMqUri;
                return;
            }

            Log.Logger.Warning("RabbitMQ URI setting is not a valid amqp/amqps URI. Falling back to host/port settings.");
        }

        /// <summary>
        /// Dead-letter retention, 30 days in milliseconds. Must be <see cref="long"/>: the value is
        /// 2,592,000,000, which overflows <see cref="int"/>.
        /// </summary>
        private const long DeadLetterRetention = 30L * 24 * 60 * 60 * 1000;

        /// <summary>
        /// Opens the connection, retrying a broker that is not answering yet.
        /// </summary>
        /// <remarks>
        /// The worker hosted services resolve this manager at startup, so an exception here used to
        /// take the whole process down — a broker restart, or a worker that simply booted first,
        /// was enough. <c>AutomaticRecoveryEnabled</c> does not help: it recovers a connection that
        /// was once established, and there is nothing to recover from a first attempt that failed.
        ///
        /// Bounded on purpose. The API resolves this manager on the first request that publishes,
        /// so the total wait is also how long that request hangs before it can answer 503; the
        /// request timeout is 30 seconds and this stays well inside it.
        /// </remarks>
        private IConnection ConnectWithRetry(ConnectionFactory factory)
        {
            var delay = InitialConnectRetryDelay;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return factory.CreateConnectionAsync().GetAwaiter().GetResult();
                }
                catch (Exception exception) when (attempt < MaxConnectAttempts)
                {
                    Log.Logger.Warning(
                        "Broker not reachable on attempt {Attempt}/{MaxAttempts}, retrying in {Delay}s. ErrorType={ErrorType}",
                        attempt,
                        MaxConnectAttempts,
                        delay.TotalSeconds,
                        GetSafeExceptionType(exception));

                    Thread.Sleep(delay);

                    // Jittered so that several workers restarting together do not retry in lockstep.
                    var next = delay.TotalSeconds * 2 * (0.75 + (Random.Shared.NextDouble() * 0.5));
                    delay = TimeSpan.FromSeconds(Math.Min(next, MaxConnectRetryDelay.TotalSeconds));
                }
            }
        }

        private async Task<IChannel> CreatePublishChannelAsync(CancellationToken cancellationToken)
        {
            var channel = await _connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true,
                    outstandingPublisherConfirmationsRateLimiter: null,
                    consumerDispatchConcurrency: null),
                cancellationToken);

            channel.BasicReturnAsync += (_, returned) =>
            {
                UnroutableMessages.WithLabels(_channelSettings.QueueName).Inc();
                Log.Logger.Error(
                    "Broker returned an unroutable message. Exchange={Exchange} RoutingKey={RoutingKey} Reply={ReplyCode} {ReplyText}",
                    returned.Exchange,
                    returned.RoutingKey,
                    returned.ReplyCode,
                    returned.ReplyText);
                return Task.CompletedTask;
            };

            return channel;
        }

        private async Task<IChannel> RentPublishChannelAsync(CancellationToken cancellationToken)
        {
            while (_publishChannels.TryTake(out var pooled))
            {
                if (pooled.IsOpen)
                    return pooled;

                pooled.Dispose();
            }

            return await CreatePublishChannelAsync(cancellationToken);
        }

        private void ReturnPublishChannel(IChannel channel)
        {
            // Closed channels are dropped rather than pooled: a channel is killed by the broker on
            // any protocol error, and handing a dead one to the next publisher turns one failure
            // into a run of them.
            if (channel.IsOpen && _publishChannels.Count < MaxPooledPublishChannels)
            {
                _publishChannels.Add(channel);
                return;
            }

            channel.Dispose();
        }

        public void SetupChannel()
        {
            _channelLock.Wait();
            try
            {
                SetupChannelCore();
            }
            finally
            {
                _channelLock.Release();
            }
        }

        private void SetupChannelCore()
        {
            try
            {
                var dlqArgs = new Dictionary<string, object?>
                {
                    { "x-message-ttl", DeadLetterRetention },
                    { "x-max-length", 100_000 },

                    // drop-head, not reject-publish: dead-lettering is not a publisher-confirm
                    // path, so rejecting would discard the failure silently at the broker.
                    { "x-overflow", "drop-head" }
                };

                _channel.ExchangeDeclareAsync(_channelSettings.ExchangeNameDLQ, "topic").GetAwaiter().GetResult();
                _channel.QueueDeclareAsync(_channelSettings.QueueNameDLQ, true, false, false, dlqArgs).GetAwaiter().GetResult();
                _channel.QueueBindAsync(_channelSettings.QueueNameDLQ, _channelSettings.ExchangeNameDLQ, _channelSettings.RoutingKeyDLQ).GetAwaiter().GetResult();

                // Setup main queue
                var queueArgs = new Dictionary<string, object?>
                {
                    { "x-queue-type", "classic" },
                    { "x-dead-letter-exchange", _channelSettings.ExchangeNameDLQ},
                    { "x-dead-letter-routing-key", _channelSettings.RoutingKeyDLQ},

                    // There is no x-message-ttl here any more, and that is the point. It used to be
                    // 12000 - twelve seconds of sitting in the queue before the broker dead-lettered
                    // the message into a queue nothing reads. A work queue's job is to hold work
                    // until someone does it; a deploy, a restart or a backlog of six messages was
                    // enough to drain it into a black hole, silently. Do not reintroduce it.
                    //
                    // Bounded by length instead, which is the honest version of the same intent:
                    // when the queue is full the broker REJECTS the publish rather than discarding
                    // something already accepted. That reaches the caller as an exception because
                    // publisher confirms are on - back-pressure the publisher can see, instead of
                    // silent loss it cannot.
                    { "x-max-length", 100_000 },
                    { "x-overflow", "reject-publish" }

                    // x-max-retries is gone too: it is not a RabbitMQ queue argument and never did
                    // anything. Retries are counted in the x-retry-count header by TryScheduleRetryAsync.
                };

                _channel.ExchangeDeclareAsync(_channelSettings.ExchangeName, "topic").GetAwaiter().GetResult();
                _channel.QueueDeclareAsync(_channelSettings.QueueName, true, false, false, queueArgs).GetAwaiter().GetResult();
                _channel.QueueBindAsync(_channelSettings.QueueName, _channelSettings.ExchangeName, _channelSettings.RoutingKey).GetAwaiter().GetResult();

                // Prefetch, not 1. With a long-lived consumer (ConsumeAsync) the broker keeps this
                // many messages in flight, so the consumer never waits a network round trip between
                // messages. Combined with ConsumerDispatchConcurrency on the connection this is what
                // takes the drain rate from ~0.4/s to the handler's own speed.
                _channel.BasicQosAsync(0, PrefetchCount, false).GetAwaiter().GetResult();
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 406)
            {
                // 406 PRECONDITION_FAILED on a declare means the queue already exists with
                // DIFFERENT arguments. Queue arguments are immutable, so this is not something the
                // application can fix at runtime and retrying will fail identically for ever.
                //
                // Without naming it, this surfaces as "OperationInterruptedException" and a failing
                // health check, which says nothing about the cause. It happens in exactly one
                // situation worth calling out: the queue was created by an older build - for
                // instance when x-message-ttl was removed from this method - and still carries the
                // old arguments.
                Log.Logger.Error(
                    "Queue '{Queue}' already exists with different arguments, so it cannot be declared. " +
                    "Queue arguments are immutable: drain and delete the queue (and '{Dlq}') on the broker, " +
                    "then restart - it will be recreated with the current arguments. Broker said: {ReplyText}",
                    _channelSettings.QueueName,
                    _channelSettings.QueueNameDLQ,
                    ex.ShutdownReason?.ReplyText);
                throw;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(
                    "Error setting up channel. ErrorType={ErrorType}",
                    GetSafeExceptionType(ex));
                throw;
            }
        }

        public async Task EnqueueAsync<T>(T obj)
        {
            // Outside the try below on purpose. A payload that will not serialize is a bug in the
            // caller, not an unavailable broker, and wrapping it as one would answer 503 with a
            // Retry-After for a request that can never succeed.
            var message = JsonSerializer.Serialize(obj, _jsonOptions);
            var body = Encoding.UTF8.GetBytes(message);

            try
            {
                var channel = await RentPublishChannelAsync(CancellationToken.None);
                try
                {
                    await channel.BasicPublishAsync(
                    exchange: _channelSettings.ExchangeName,
                    routingKey: _channelSettings.RoutingKey,
                    mandatory: true,
                    basicProperties: new BasicProperties()
                    {
                        // Durable queue plus a transient message is not durable. Without this the
                        // broker holds the message in memory only and discards the lot on restart,
                        // which is exactly the case the queue exists to survive. DeliveryMode
                        // defaults to 0 on a new BasicProperties, so this has to be said out loud.
                        Persistent = true,
                        MessageId = Guid.NewGuid().ToString("N"),
                        Headers = new Dictionary<string, object?>
                        {
                            ["x-retry-count"] = 0
                        }
                    },
                    body: body);
                }
                finally
                {
                    ReturnPublishChannel(channel);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(
                    "Error publishing message. ErrorType={ErrorType}",
                    GetSafeExceptionType(ex));

                throw new MessageBrokerUnavailableException("The message broker did not accept the message.", ex);
            }
        }

        /// <summary>
        /// Registers ONE consumer and keeps it for the caller's lifetime, processing messages as
        /// they arrive until the token is cancelled.
        /// <para>
        /// This replaces the loop the workers used to run:
        /// <c>DequeueAndProcessAsync</c> served a single message, tore the consumer down, and the
        /// worker then slept two to five seconds before doing it again. That capped every queue at
        /// roughly 0.4 messages a second regardless of load - a full declare/consume/cancel round
        /// trip plus a sleep for each message - and it was the reason a modest burst could not be
        /// drained. Here the consumer is registered once, the broker pushes up to
        /// <see cref="PrefetchCount"/> messages ahead, and throughput becomes whatever the handler
        /// itself can manage.
        /// </para>
        /// <para>
        /// Handlers may now run CONCURRENTLY, because the connection sets ConsumerDispatchConcurrency
        /// above one. A handler that was safe only because it was called one at a time is no longer
        /// safe here.
        /// </para>
        /// </summary>
        public async Task ConsumeAsync<T>(Func<T, string, Task> processMessageAsync, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(processMessageAsync);

            Log.Logger.Information(
                "Starting long-lived consumer on {Queue} with prefetch {Prefetch}",
                _channelSettings.QueueName,
                PrefetchCount);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += (_, ea) => HandleDeliveryAsync(ea, processMessageAsync);

            var consumerTag = await StartConsumerAsync(consumer, cancellationToken);

            try
            {
                // The consumer runs on the client's dispatch threads; this loop only keeps the
                // method alive and refreshes the depth gauges, which no longer have a per-message
                // dequeue call to hang off.
                while (!cancellationToken.IsCancellationRequested)
                {
                    await RefreshQueueDepthsAsync(cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown, not a fault.
            }
            finally
            {
                try
                {
                    await CancelConsumerAsync(consumerTag);
                }
                catch (Exception exception)
                {
                    Log.Logger.Warning(
                        "Could not cancel consumer cleanly on shutdown. ErrorType={ErrorType}",
                        GetSafeExceptionType(exception));
                }
            }
        }

        /// <summary>
        /// One delivery: deserialize, hand to the caller, acknowledge. On failure it counts the
        /// attempt, then either reschedules a retry or nacks the message to the dead-letter queue.
        /// Shared by <see cref="ConsumeAsync"/> and the single-message DequeueAndProcessAsync so the
        /// two cannot drift apart on acknowledgement or retry behaviour.
        /// </summary>
        private async Task HandleDeliveryAsync<T>(BasicDeliverEventArgs ea, Func<T, string, Task> processMessageAsync)
        {
            try
            {
                var message = Encoding.UTF8.GetString(ea.Body.ToArray());
                var resultObject = JsonSerializer.Deserialize<T>(message, _jsonOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize message payload.");

                // Empty rather than a fresh guid when the broker sends no id: a handler must be
                // able to tell "this message has no identity" from "this is a new one", and a
                // generated value would look unique on every redelivery.
                var messageId = ea.BasicProperties?.MessageId ?? string.Empty;

                using (MessageDuration.WithLabels(_channelSettings.QueueName).NewTimer())
                    await processMessageAsync(resultObject, messageId);

                MessagesProcessed.WithLabels(_channelSettings.QueueName, "ok").Inc();
                await AckAsync(ea.DeliveryTag);
            }
            catch (Exception ex)
            {
                // Counted before the retry decision: a message that succeeds on its third attempt
                // still cost two failures, and a rate that only counts final give-ups hides a queue
                // that is limping.
                MessagesProcessed.WithLabels(_channelSettings.QueueName, "failed").Inc();

                try
                {
                    if (await TryScheduleRetryAsync(ea, ex))
                        return;

                    await NackAsync(ea.DeliveryTag);
                }
                catch (Exception nackException)
                {
                    Log.Logger.Error(
                        "Error nacking message. ErrorType={ErrorType}",
                        GetSafeExceptionType(nackException));
                }
            }
        }

        /// <summary>
        /// Moves up to <paramref name="maxMessages"/> messages from the dead-letter queue back onto
        /// the work queue, resetting their retry count.
        /// <para>
        /// Nothing consumes the dead-letter queue, so without this a message that failed three times
        /// is invisible and permanent - the depth gauge made it visible, which is not the same as
        /// recoverable.
        /// </para>
        /// <para>
        /// Deliberately operator-triggered and bounded rather than automatic. A redrive is a bulk
        /// retry, and whether re-processing a message is safe depends on whether the handler is
        /// idempotent - which is an open question for this pipeline. Putting a person and a batch
        /// size in front of it keeps that decision where it belongs until the question is answered.
        /// </para>
        /// <para>
        /// A message is acknowledged on the dead-letter queue only after it has been accepted onto
        /// the work queue, and publisher confirms mean that acceptance is the broker's, not a
        /// hopeful write to a socket. The order matters: the other way round loses the message if
        /// the republish fails.
        /// </para>
        /// </summary>
        /// <returns>How many messages were actually moved.</returns>
        public async Task<int> RedriveDeadLetteredAsync(int maxMessages, CancellationToken cancellationToken = default)
        {
            if (maxMessages <= 0)
                return 0;

            var moved = 0;

            for (var i = 0; i < maxMessages && !cancellationToken.IsCancellationRequested; i++)
            {
                var delivery = await GetFromDeadLetterAsync(cancellationToken);
                if (delivery is null)
                    break; // Queue is empty - nothing further to move.

                try
                {
                    var headers = CloneHeaders(delivery.BasicProperties?.Headers);

                    // Reset the counter: this message has been looked at by a person and is being
                    // given a fresh set of attempts, not resuming an exhausted one.
                    headers["x-retry-count"] = 0;

                    var redriveChannel = await RentPublishChannelAsync(cancellationToken);
                    try
                    {
                        await redriveChannel.BasicPublishAsync(
                        exchange: _channelSettings.ExchangeName,
                        routingKey: _channelSettings.RoutingKey,
                        mandatory: true,
                        basicProperties: new BasicProperties
                        {
                            Persistent = true,
                            MessageId = delivery.BasicProperties?.MessageId ?? Guid.NewGuid().ToString("N"),
                            Headers = headers
                        },
                        body: delivery.Body,
                        cancellationToken: cancellationToken);
                    }
                    finally
                    {
                        ReturnPublishChannel(redriveChannel);
                    }

                    await AckAsync(delivery.DeliveryTag, cancellationToken);

                    MessagesRedriven.WithLabels(_channelSettings.QueueNameDLQ).Inc();
                    moved++;
                }
                catch (Exception exception)
                {
                    // Put it back where it was. Requeue rather than drop: a redrive that fails must
                    // leave the dead-letter queue exactly as it found it.
                    Log.Logger.Error(
                        "Redrive failed, returning the message to {Dlq}. ErrorType={ErrorType}",
                        _channelSettings.QueueNameDLQ,
                        GetSafeExceptionType(exception));

                    await NackAsync(delivery.DeliveryTag, requeue: true, cancellationToken);
                    throw;
                }
            }

            if (moved > 0)
            {
                Log.Logger.Warning(
                    "Redrove {Count} message(s) from {Dlq} back onto {Queue}",
                    moved,
                    _channelSettings.QueueNameDLQ,
                    _channelSettings.QueueName);
            }

            await RefreshQueueDepthsAsync(cancellationToken);

            return moved;
        }

        public async Task<T> DequeueAndProcessAsync<T>()
        {
            T? processedMessage = default;
            var hasProcessedMessage = false;

            await DequeueAndProcessAsync<T>(message =>
            {
                processedMessage = message;
                hasProcessedMessage = true;
                return Task.CompletedTask;
            });

            if (!hasProcessedMessage)
                throw new InvalidOperationException("No message was processed from queue.");

            return processedMessage!;
        }

        public async Task DequeueAndProcessAsync<T>(Func<T, Task> processMessageAsync, CancellationToken cancellationToken = default)
        {
            if (processMessageAsync is null)
                throw new ArgumentNullException(nameof(processMessageAsync));

            Log.Logger.Information("Starting Dequeue and Process");
            await RefreshQueueDepthsAsync(cancellationToken);
            var consumer = new AsyncEventingBasicConsumer(_channel);
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration cancellationRegistration = default;
            var consumerCancelled = 0;

            AsyncEventHandler<BasicDeliverEventArgs>? handler = null;
            handler = async (_, ea) =>
            {
                try
                {
                    Log.Logger.Information("Consuming message");
                    var body = ea.Body;
                    var message = Encoding.UTF8.GetString(body.ToArray());
                    var resultObject = JsonSerializer.Deserialize<T>(message, _jsonOptions);

                    if (resultObject is null)
                        throw new InvalidOperationException("Failed to deserialize message payload.");

                    using (MessageDuration.WithLabels(_channelSettings.QueueName).NewTimer())
                        await processMessageAsync(resultObject);

                    MessagesProcessed.WithLabels(_channelSettings.QueueName, "ok").Inc();
                    await TryCancelConsumerAsync(ea.ConsumerTag);
                    await AckAsync(ea.DeliveryTag);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    // Counted before the retry decision: a message that succeeds on its third
                    // attempt still cost two failures, and a failure rate that only counts final
                    // give-ups hides a queue that is limping.
                    MessagesProcessed.WithLabels(_channelSettings.QueueName, "failed").Inc();
                    await TryCancelConsumerAsync(ea.ConsumerTag);

                    try
                    {
                        if (await TryScheduleRetryAsync(ea, ex))
                        {
                            tcs.TrySetResult(true);
                            return;
                        }

                        await NackAsync(ea.DeliveryTag);
                    }
                    catch (Exception nackException)
                    {
                        Log.Logger.Error(
                            "Error nacking message. ErrorType={ErrorType}",
                            GetSafeExceptionType(nackException));
                    }

                    tcs.TrySetException(ex);
                }
                finally
                {
                    consumer.ReceivedAsync -= handler;
                }
            };

            consumer.ReceivedAsync += handler;

            try
            {
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationRegistration = cancellationToken.Register(() =>
                    {
                        tcs.TrySetCanceled(cancellationToken);
                    });
                }

                await StartConsumerAsync(consumer, cancellationToken);

                await tcs.Task;
            }
            finally
            {
                consumer.ReceivedAsync -= handler;
                cancellationRegistration.Dispose();
                await TryCancelConsumerAsync();
            }

            async Task TryCancelConsumerAsync(string? deliveredConsumerTag = null)
            {
                if (Interlocked.Exchange(ref consumerCancelled, 1) == 1)
                    return;

                var consumerTags = string.IsNullOrWhiteSpace(deliveredConsumerTag)
                    ? consumer.ConsumerTags.ToArray()
                    : [deliveredConsumerTag];

                foreach (var consumerTag in consumerTags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct())
                {
                    try
                    {
                        await CancelConsumerAsync(consumerTag);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Warning(
                            "Error canceling RabbitMQ consumer. ErrorType={ErrorType}",
                            GetSafeExceptionType(ex));
                    }
                }
            }
        }

        private static string GetSafeExceptionType(Exception? exception)
        {
            return exception?.GetType().Name ?? "UnknownError";
        }

        private async Task<bool> TryScheduleRetryAsync(BasicDeliverEventArgs eventArgs, Exception processingException)
        {
            var currentRetryCount = GetRetryCount(eventArgs.BasicProperties);
            if (currentRetryCount >= MaxProcessingRetries)
                return false;

            var nextRetryCount = currentRetryCount + 1;
            var headers = CloneHeaders(eventArgs.BasicProperties?.Headers);
            headers["x-retry-count"] = nextRetryCount;

            var retryChannel = await RentPublishChannelAsync(CancellationToken.None);
            try
            {
                await retryChannel.BasicPublishAsync(
                exchange: _channelSettings.ExchangeName,
                routingKey: _channelSettings.RoutingKey,
                mandatory: true,
                basicProperties: new BasicProperties
                {
                    // Same reasoning as the first publish. Missing here it would mean a message
                    // becomes less durable every time it is retried - the opposite of the intent.
                    Persistent = true,
                    MessageId = eventArgs.BasicProperties?.MessageId ?? Guid.NewGuid().ToString("N"),
                    Headers = headers
                },
                body: eventArgs.Body);
            }
            finally
            {
                ReturnPublishChannel(retryChannel);
            }

            await AckAsync(eventArgs.DeliveryTag);

            Log.Logger.Warning(
                "Message processing failed. Retrying message attempt {RetryAttempt}/{MaxRetries}. ErrorType={ErrorType}",
                nextRetryCount,
                MaxProcessingRetries,
                GetSafeExceptionType(processingException));

            return true;
        }

        private async Task<string> StartConsumerAsync(IAsyncBasicConsumer consumer, CancellationToken cancellationToken)
        {
            await _channelLock.WaitAsync(cancellationToken);
            try
            {
                return await _channel.BasicConsumeAsync(
                    queue: _channelSettings.QueueName,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: cancellationToken);
            }
            finally
            {
                _channelLock.Release();
            }
        }

        private async Task CancelConsumerAsync(string consumerTag)
        {
            await _channelLock.WaitAsync(CancellationToken.None);
            try
            {
                await _channel.BasicCancelAsync(consumerTag, false, CancellationToken.None);
            }
            finally
            {
                _channelLock.Release();
            }
        }

        private async Task AckAsync(ulong deliveryTag, CancellationToken cancellationToken = default)
        {
            await _channelLock.WaitAsync(cancellationToken);
            try
            {
                await _channel.BasicAckAsync(deliveryTag, false, cancellationToken);
            }
            finally
            {
                _channelLock.Release();
            }
        }

        private async Task NackAsync(ulong deliveryTag, bool requeue = false, CancellationToken cancellationToken = default)
        {
            await _channelLock.WaitAsync(cancellationToken);
            try
            {
                await _channel.BasicNackAsync(deliveryTag, false, requeue, cancellationToken);
            }
            finally
            {
                _channelLock.Release();
            }
        }

        private async Task<BasicGetResult?> GetFromDeadLetterAsync(CancellationToken cancellationToken)
        {
            await _channelLock.WaitAsync(cancellationToken);
            try
            {
                return await _channel.BasicGetAsync(_channelSettings.QueueNameDLQ, autoAck: false, cancellationToken);
            }
            finally
            {
                _channelLock.Release();
            }
        }

        private static Dictionary<string, object?> CloneHeaders(IDictionary<string, object?>? sourceHeaders)
        {
            if (sourceHeaders == null || sourceHeaders.Count == 0)
                return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            return new Dictionary<string, object?>(sourceHeaders, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Refreshes both depth gauges. Called on each dequeue pass rather than on a timer of its
        /// own: the worker already polls, so this costs one extra round trip on a path that is
        /// nowhere near saturated, and it needs no second schedule to reason about.
        /// </summary>
        private async Task RefreshQueueDepthsAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _channelLock.WaitAsync(cancellationToken);
                try
                {
                    var main = await _channel.QueueDeclarePassiveAsync(_channelSettings.QueueName, cancellationToken);
                    QueueDepth.WithLabels(_channelSettings.QueueName).Set(main.MessageCount);

                    var dlq = await _channel.QueueDeclarePassiveAsync(_channelSettings.QueueNameDLQ, cancellationToken);
                    DlqDepth.WithLabels(_channelSettings.QueueNameDLQ).Set(dlq.MessageCount);
                }
                finally
                {
                    _channelLock.Release();
                }
            }
            catch (Exception exception)
            {
                // Never fail a dequeue because a gauge could not be read.
                Log.Logger.Warning(
                    "Could not read queue depths. ErrorType={ErrorType}",
                    GetSafeExceptionType(exception));
            }
        }

        private static int GetRetryCount(IReadOnlyBasicProperties? basicProperties)
        {
            var headers = basicProperties?.Headers;
            if (headers == null || !headers.TryGetValue("x-retry-count", out var rawRetryCount))
                return 0;

            return TryParseHeaderInt(rawRetryCount, out var retryCount) && retryCount >= 0
                ? retryCount
                : 0;
        }

        private static bool TryParseHeaderInt(object? rawValue, out int parsedValue)
        {
            parsedValue = 0;
            if (rawValue is null)
                return false;

            switch (rawValue)
            {
                case int intValue:
                    parsedValue = intValue;
                    return true;
                case long longValue when longValue is <= int.MaxValue and >= int.MinValue:
                    parsedValue = (int)longValue;
                    return true;
                case byte byteValue:
                    parsedValue = byteValue;
                    return true;
                case byte[] bytesValue when int.TryParse(Encoding.UTF8.GetString(bytesValue), out var parsedFromBytes):
                    parsedValue = parsedFromBytes;
                    return true;
                case ReadOnlyMemory<byte> memoryValue when int.TryParse(Encoding.UTF8.GetString(memoryValue.ToArray()), out var parsedFromMemory):
                    parsedValue = parsedFromMemory;
                    return true;
                case string stringValue when int.TryParse(stringValue, out var parsedFromString):
                    parsedValue = parsedFromString;
                    return true;
                default:
                    return false;
            }
        }
    }
}
