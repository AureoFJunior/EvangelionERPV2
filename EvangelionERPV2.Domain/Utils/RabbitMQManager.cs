using Microsoft.Extensions.Configuration;
using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using EvangelionERPV2.Domain.Interfaces;
using EvangelionERPV2.Domain.Models.RabbitMQ;
using Serilog;
using Microsoft.Extensions.Options;

namespace EvangelionERPV2.Domain.Utils
{
    public class RabbitMQManager : IRabbitMQManager
    {
        public readonly IConfiguration _configuration;
        public readonly IConnection _connection;
        public readonly RabbitMQSettings _rabbitMQSettings;
        private IModel? _channel;

        public RabbitMQManager(IConfiguration configuration, IOptions<RabbitMQSettings> rabbitMQSettings)
        {
            _configuration = configuration;
            _rabbitMQSettings = rabbitMQSettings.Value;

            Log.Logger.Information($"Building conn factory");
            var factory = new ConnectionFactory
            {
                HostName = _rabbitMQSettings.HostName,
                UserName = _rabbitMQSettings.UserName,
                Password = _rabbitMQSettings.Password,
                VirtualHost = _rabbitMQSettings.VirtualHost,
                Port = _rabbitMQSettings.Port,
                Uri = new Uri(_rabbitMQSettings.Uri)
            };

            _connection = factory.CreateConnection();
        }

        #region RabbitMQ
        public void Enqueue<T>(T obj, BaseChannelSettings channelSettings)
        {
            Log.Logger.Information($"Publishing message");
            using var channel = GetChannel(channelSettings);

            string message = JsonSerializer.Serialize(obj);
            var body = Encoding.UTF8.GetBytes(message);

            channel.BasicPublish(exchange: channelSettings.ExchangeName,
                                 routingKey: channelSettings.RoutingKeyDLQ,
                                 basicProperties: null,
                                 body: body);

            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
        }

        public void Enqueue<T>(IEnumerable<T> obj, BaseChannelSettings channelSettings)
        {
            Log.Logger.Information($"Publishing message");
            using var channel = GetChannel(channelSettings);

            string message = JsonSerializer.Serialize(obj);
            var body = Encoding.UTF8.GetBytes(message);

            channel.BasicPublish(exchange: channelSettings.ExchangeName,
                                 routingKey: channelSettings.QueueName,
                                 basicProperties: null,
                                 body: body);

            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));

        }

        public Task EnqueueAsync<T>(T obj, BaseChannelSettings channelSettings)
        {
            return Task.Run(() => Enqueue(obj, channelSettings));
        }

        public Task EnqueueAsync<T>(IEnumerable<T> obj, BaseChannelSettings channelSettings)
        {
            return Task.Run(() => Enqueue(obj, channelSettings));
        }

        public async Task<T> Dequeue<T>(BaseChannelSettings channelSettings)
        {
            Log.Logger.Information($"Consuming message");
            using var channel = GetChannel(channelSettings);
            T obj = default(T);
            var consumer = new AsyncEventingBasicConsumer(channel);
            var tcs = new TaskCompletionSource<T>();

            consumer.Received += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body;
                    var message = Encoding.UTF8.GetString(body.ToArray());
                    obj = JsonSerializer.Deserialize<T>(message);
                    channel.BasicAck(ea.DeliveryTag, false);
                    tcs.SetResult(obj);
                }
                catch (Exception ex)
                {
                    Log.Logger.Error($"Error when consuming message: {ex.Message}", ex);
                    channel.BasicNack(ea.DeliveryTag, false, true);
                }

            };

            channel.BasicConsume(queue: channelSettings.QueueName,
                                 autoAck: true,
                                 consumer: consumer);
            return await tcs.Task;
        }

        public void DeclareQueue(BaseChannelSettings channelSettings, bool durable = true, bool exclusive = false, bool autoDelete = false)
        {
            try
            {
                Log.Logger.Information($"Starting queue declare");
                var factory = new ConnectionFactory()
                {
                    HostName = _rabbitMQSettings.HostName,
                    DispatchConsumersAsync = true
                };

                var connection = factory.CreateConnection();
                var channel = connection.CreateModel();

                Log.Logger.Information($"Declaring exchange [{channelSettings.ExchangeName}]");
                Log.Logger.Information($"Declaring queue [{channelSettings.QueueName}]");
                Log.Logger.Information($"Declaring routing key [{channelSettings.RoutingKey}]");

                channel.ExchangeDeclare(channelSettings.ExchangeName, "topic");
                channel.QueueDeclare(queue: channelSettings.QueueName, durable, exclusive, autoDelete, arguments: null);
                channel.QueueBind(channelSettings.QueueName, channelSettings.ExchangeName, channelSettings.RoutingKey, null);
                channel.ConfirmSelect();

                var queueArgs = new Dictionary<string, object>
                {
                { "x-dead-letter-exchange", channelSettings.ExchangeName},
                { "x-dead-letter-routing-key", channelSettings.RoutingKey},
                { "x-message-ttl", "60000"},
                { "x-max-retries", "3"}
                };

                Log.Logger.Information($"Declaring DLQ [{channelSettings.QueueNameDLQ}]");
                channel.ExchangeDeclare(channelSettings.ExchangeNameDLQ, "direct");
                channel.QueueDeclare(queue: channelSettings.QueueNameDLQ, durable, exclusive, autoDelete, queueArgs);
                channel.QueueBind(channelSettings.QueueNameDLQ, channelSettings.ExchangeNameDLQ, channelSettings.RoutingKeyDLQ, null);
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error at queue declare: {ex.Message}", ex);
                throw;
            }
        }

        public IModel? GetChannel(BaseChannelSettings channelSettings)
        {
            try
            {
                if (_channel == null || _channel.IsClosed)
                {
                    _channel = _connection?.CreateModel();

                    if (_channel == null)
                        Log.Logger.Error("Could not create channel.");
                    else
                        DeclareQueue(channelSettings);
                }

                return _channel;
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error creating channel: {ex.Message}");
                throw;
            }
        }

        public async Task<T> DequeueAsync<T>(BaseChannelSettings channelSettings)
        {
            var dequeuedObject = await Dequeue<T>(channelSettings);
            return dequeuedObject;
        }

        public async Task<IEnumerable<T>> DequeueList<T>(BaseChannelSettings channelSettings)
        {
            Log.Logger.Information($"Consuming message");
            IEnumerable<T> obj = default(IEnumerable<T>);
            using var channel = GetChannel(channelSettings);

            var consumer = new AsyncEventingBasicConsumer(channel);
            var tcs = new TaskCompletionSource<bool>();
            consumer.Received += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body;
                    var message = Encoding.UTF8.GetString(body.ToArray());
                    obj.Append(JsonSerializer.Deserialize<T>(message));
                    channel.BasicAck(ea.DeliveryTag, false);

                    if (SharedFunctions.IsNotNullOrEmpty(obj))
                        tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    Log.Logger.Error($"Error when consuming message: {ex.Message}", ex);
                    channel.BasicNack(ea.DeliveryTag, false, true);
                }

            };

            channel.BasicConsume(channelSettings.QueueName, true, consumer);
            await tcs.Task;
            return obj;

        }

        public async Task<IEnumerable<T>> DequeueListAsync<T>(BaseChannelSettings channelSettings)
        {
            var dequeuedObject = await Dequeue<IEnumerable<T>>(channelSettings);
            return dequeuedObject;
        }

        public async Task<T> DequeueAndProcess<T>(BaseChannelSettings channelSettings)
        {
            Log.Logger.Information($"Consuming message");
            T obj = default(T);
            using var channel = GetChannel(channelSettings);

            var consumer = new AsyncEventingBasicConsumer(channel);
            var tcs = new TaskCompletionSource<T>();
            consumer.Received += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body;
                    var message = Encoding.UTF8.GetString(body.ToArray());
                    obj = JsonSerializer.Deserialize<T>(message);
                    channel.BasicAck(ea.DeliveryTag, false);
                    tcs.SetResult(obj);
                }
                catch (Exception ex)
                {
                    channel.BasicNack(ea.DeliveryTag, false, true);
                }

            };

            channel.BasicConsume(channelSettings.QueueName, true, consumer);

            await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            return obj;

        }

        public async Task<T> DequeueAndProcessAsync<T>(BaseChannelSettings channelSettings)
        {
            var dequeuedObject = await DequeueAndProcess<T>(channelSettings);
            return dequeuedObject;
        }
        #endregion
    }
}
