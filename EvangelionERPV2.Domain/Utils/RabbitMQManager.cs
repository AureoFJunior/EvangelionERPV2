using Microsoft.Extensions.Configuration;
using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using EvangelionERPV2.Domain.Interfaces;

namespace EvangelionERPV2.Domain.Utils
{
    public class RabbitMQManager : IRabbitMQManager
    {
        public readonly IConfiguration _configuration;

        public RabbitMQManager(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        #region RabbitMQ
        public void Enqueue<T>(T obj, string queueName)
        {
            //RabbitMQConfiguration rabbitMQConfiguration = new RabbitMQConfiguration(_configuration);
            //var factory = new ConnectionFactory()
            //{
            //    HostName = rabbitMQConfiguration.QueueHostName,
            //    DispatchConsumersAsync = true

            //};
            //var connection = factory.CreateConnection();

            //var channel = GetChannel(queueName);

            //string message = JsonSerializer.Serialize(obj);
            //var body = Encoding.UTF8.GetBytes(message);

            //channel.BasicPublish(exchange: "",
            //                     routingKey: queueName,
            //                     basicProperties: null,
            //                     body: body);

            //channel.Close();
            //connection.Close();

        }

        public void Enqueue<T>(IEnumerable<T> obj, string queueName)
        {
            //RabbitMQConfiguration rabbitMQConfiguration = new RabbitMQConfiguration(_configuration);
            //var factory = new ConnectionFactory()
            //{
            //    HostName = rabbitMQConfiguration.QueueHostName,
            //    DispatchConsumersAsync = true

            //};
            //using (var connection = factory.CreateConnection())
            //using (var channel = connection.CreateModel())
            //{
            //    channel.QueueDeclare(queue: queueName,
            //                         durable: false,
            //                         exclusive: false,
            //                         autoDelete: false,
            //                         arguments: null);

            //    string message = JsonSerializer.Serialize(obj);
            //    var body = Encoding.UTF8.GetBytes(message);

            //    channel.BasicPublish(exchange: "",
            //                         routingKey: queueName,
            //                         basicProperties: null,
            //                         body: body);

            //    channel.Close();
            //    connection.Close();
            //}
        }

        public Task EnqueueAsync<T>(T obj, string queueName)
        {
            return Task.Run(() => Enqueue(obj, queueName));
        }

        public Task EnqueueAsync<T>(IEnumerable<T> obj, string queueName)
        {
            return Task.Run(() => Enqueue(obj, queueName));
        }

        public async Task<T> Dequeue<T>(string queueName)
        {
            var channel = GetChannel(queueName);
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
                    channel.BasicNack(ea.DeliveryTag, false, true);
                }

            };

            channel.BasicConsume(queue: queueName,
                                 autoAck: true,
                                 consumer: consumer);
            return await tcs.Task;
        }

        public IModel? GetChannel(string queueName)
        {
            //RabbitMQConfiguration rabbitMQConfiguration = new RabbitMQConfiguration(_configuration);
            //var factory = new ConnectionFactory()
            //{
            //    HostName = rabbitMQConfiguration.QueueHostName,
            //    DispatchConsumersAsync = true

            //};

            //var connection = factory.CreateConnection();
            //var channel = connection.CreateModel();

            //channel.ExchangeDeclare();
            //channel.QueueDeclare(queue: queueName,
            //                     durable: false,
            //                     exclusive: false,
            //                     autoDelete: false,
            //arguments: null);
            //channel.ExchangeBind();
            //channel.ConfirmSelect();

            //return channel;
            return null;
        }

        public async Task<T> DequeueAsync<T>(string queueName)
        {
            var dequeuedObject = await Dequeue<T>(queueName);
            return dequeuedObject;
        }

        public async Task<IEnumerable<T>> DequeueList<T>(string queueName)
        {

            IEnumerable<T> obj = default(IEnumerable<T>);
            var channel = GetChannel(queueName);

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
                    channel.BasicNack(ea.DeliveryTag, false, true);
                }

            };

            channel.BasicConsume(queueName, true, consumer);
            await tcs.Task;
            return obj;

        }

        public async Task<IEnumerable<T>> DequeueListAsync<T>(string queueName)
        {
            var dequeuedObject = await Dequeue<IEnumerable<T>>(queueName);
            return dequeuedObject;
        }

        public async Task<T> DequeueAndProcess<T>(string queueName)
        {

            T obj = default(T);
            var channel = GetChannel(queueName);

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

            channel.BasicConsume(queueName, true, consumer);

            await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            return obj;

        }

        public async Task<T> DequeueAndProcessAsync<T>(string queueName)
        {
            var dequeuedObject = await DequeueAndProcess<T>(queueName);
            return dequeuedObject;
        }
        #endregion
    }
}
