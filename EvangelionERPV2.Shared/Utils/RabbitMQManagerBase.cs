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
        private readonly IConnection _connection;
        private readonly IChannel _channel;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly AsyncEventingBasicConsumer _consumer;
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
                Port = SharedFunctions.SafeConvertToNumber<int>(_kmsProvider.GetKMSKey(rabbitMQSettings.Value.Port.ToString())),
                Uri = new Uri(_kmsProvider.GetKMSKey(rabbitMQSettings.Value.Uri)),
                AutomaticRecoveryEnabled = true,
                ConsumerDispatchConcurrency = 10,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            _connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
            _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();

            SetupChannel();

            _consumer = new AsyncEventingBasicConsumer(_channel);
            _channel.BasicConsumeAsync(queue: _channelSettings.QueueName,
                                autoAck: false,
                                consumer: _consumer).GetAwaiter().GetResult();

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = ReferenceHandler.Preserve,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };
            
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
                Log.Logger.Error($"Error setting up channel: {ex.Message}");
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
                    basicProperties: new BasicProperties() { },
                    body: body);
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error publishing message: {ex.Message}");
                throw;
            }
        }

        public async Task<T> DequeueAndProcessAsync<T>()
        {
            Log.Logger.Information($"Starting Dequeue and Process");
            var tcs = new TaskCompletionSource<T>();

            AsyncEventHandler<BasicDeliverEventArgs>? handler = null;
            handler = async (model, ea) =>
            {
                try
                {
                    Log.Logger.Information($"Consuming message");
                    var body = ea.Body;
                    var message = Encoding.UTF8.GetString(body.ToArray());
                    var resultObject = JsonSerializer.Deserialize<T>(message, _jsonOptions);

                    if (resultObject is null)
                        throw new InvalidOperationException("Failed to deserialize message payload.");

                    await _channel.BasicAckAsync(ea.DeliveryTag, false);

                    _consumer.ReceivedAsync -= handler;

                    tcs.TrySetResult(resultObject);
                }
                catch (Exception ex)
                {
                    await _channel.BasicNackAsync(ea.DeliveryTag, false, false);

                    _consumer.ReceivedAsync -= handler;

                    tcs.TrySetException(ex);
                }
            };

            _consumer.ReceivedAsync += handler;

            return await tcs.Task;
        }
    }
}
