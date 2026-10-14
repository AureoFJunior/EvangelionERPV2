using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using Serilog;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using EvangelionERPV2.Shared.Interfaces;
using EvangelionERPV2.Shared.Entities.RabbitMQ;

namespace EvangelionERPV2.Shared.Utils
{
    public abstract class RabbitMQManagerBase<TChannelSettings> : IRabbitMQManagerBase
    where TChannelSettings : class, IBaseChannelSettings
    {
        private const int MaxProcessingRetries = 3;
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
            _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();

            SetupChannel();

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = ReferenceHandler.Preserve,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };
            
        }

        private static int ResolveRabbitMqPort(string? rawPort)
        {
            var port = SharedFunctions.SafeConvertToNumber<int>(rawPort ?? string.Empty);
            return port > 0 ? port : 5672;
        }

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
                    { "x-message-ttl", 12000},
                    { "x-max-retries", 3}
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

                    await processMessageAsync(resultObject);
                    await TryCancelConsumerAsync(ea.ConsumerTag);
                    await _channel.BasicAckAsync(ea.DeliveryTag, false);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
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
