using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Serilog;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using EvangelionERPV2.Shared.Interfaces;
using EvangelionERPV2.Shared.Entities.RabbitMQ;
using Prometheus;

namespace EvangelionERPV2.Shared.Utils
{
    public abstract class RabbitMQManagerBase<TChannelSettings> : IRabbitMQManagerBase
    where TChannelSettings : class, IBaseChannelSettings
    {
        private const int MaxProcessingRetries = 3;

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
        private readonly IChannel _channel;
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

            _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();

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

            SetupChannel();

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = ReferenceHandler.Preserve,
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
        /// Refuses a plaintext broker URI outside Development.
        /// <para>
        /// Production was already on <c>amqps</c> when this was written - but only because the
        /// secret happened to say so. Nothing required it, nothing warned, and one edit to
        /// <c>evangelion/prd/rabbitmq</c> during an incident would have downgraded every AMQP login
        /// and every serialized Order and e-mail payload to cleartext with no signal at all.
        /// </para>
        /// <para>
        /// Failing closed here means a bad URI stops the process instead of quietly publishing
        /// customer data in the open. That is the intended trade: an outage is visible and
        /// recoverable in minutes, cleartext PII on the wire is neither.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">The URI is plaintext amqp outside Development.</exception>
        public static void EnsureSchemeIsAllowed(string scheme, string? environmentName)
        {
            if (!scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase))
                return;

            // Development only, and only when it says so explicitly. An unset environment is
            // treated as production: the safe reading of "I do not know where I am".
            if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
            {
                Log.Logger.Warning(
                    "RabbitMQ is configured over plaintext amqp. Allowed only because the environment is Development.");
                return;
            }

            throw new InvalidOperationException(
                "RabbitMQ is configured over plaintext amqp outside Development. Use amqps (port 5671): " +
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
                EnsureSchemeIsAllowed(rabbitMqUri.Scheme, CurrentEnvironmentName());
                factory.Uri = rabbitMqUri;
                return;
            }

            Log.Logger.Warning("RabbitMQ URI setting is not a valid amqp/amqps URI. Falling back to host/port settings.");
        }

        public void SetupChannel()
        {
            try
            {
                // Setup DLQ
                _channel.ExchangeDeclareAsync(_channelSettings.ExchangeNameDLQ, "topic").GetAwaiter().GetResult();
                _channel.QueueDeclareAsync(_channelSettings.QueueNameDLQ, true, false, false).GetAwaiter().GetResult();
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

                _channel.BasicQosAsync(0, 1, false).GetAwaiter().GetResult();
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
            try
            {
                var message = JsonSerializer.Serialize(obj, _jsonOptions);
                var body = Encoding.UTF8.GetBytes(message);

                await _channel.BasicPublishAsync(
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
            catch (Exception ex)
            {
                Log.Logger.Error(
                    "Error publishing message. ErrorType={ErrorType}",
                    GetSafeExceptionType(ex));
                throw;
            }
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
                    await _channel.BasicAckAsync(ea.DeliveryTag, false);
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

                        await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
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

                await _channel.BasicConsumeAsync(
                    queue: _channelSettings.QueueName,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: cancellationToken);

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
                        await _channel.BasicCancelAsync(consumerTag, false, CancellationToken.None);
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

            await _channel.BasicPublishAsync(
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

            await _channel.BasicAckAsync(eventArgs.DeliveryTag, false);

            Log.Logger.Warning(
                "Message processing failed. Retrying message attempt {RetryAttempt}/{MaxRetries}. ErrorType={ErrorType}",
                nextRetryCount,
                MaxProcessingRetries,
                GetSafeExceptionType(processingException));

            return true;
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
                var main = await _channel.QueueDeclarePassiveAsync(_channelSettings.QueueName, cancellationToken);
                QueueDepth.WithLabels(_channelSettings.QueueName).Set(main.MessageCount);

                var dlq = await _channel.QueueDeclarePassiveAsync(_channelSettings.QueueNameDLQ, cancellationToken);
                DlqDepth.WithLabels(_channelSettings.QueueNameDLQ).Set(dlq.MessageCount);
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
