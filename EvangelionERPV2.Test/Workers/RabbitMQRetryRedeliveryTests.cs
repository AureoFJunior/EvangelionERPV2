using System.Collections.Concurrent;
using System.Text;
using Amazon.SecretsManager;
using EvangelionERPV2.Shared.Entities.RabbitMQ;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;

namespace EvangelionERPV2.Test.Workers;

[Trait("Category", "E2E")]
public sealed class RabbitMQRetryRedeliveryTests
{
    [RequiresE2EBrokerFact]
    public async Task Broker_redelivery_and_retry_preserve_message_id_after_handler_side_effect()
    {
        var brokerUri = Environment.GetEnvironmentVariable("ERP_E2E_RABBITMQ_URI");
        Assert.False(string.IsNullOrWhiteSpace(brokerUri));

        var queue = $"e2e.redelivery.{Guid.NewGuid():N}";
        var exchange = $"{queue}.exchange.topic";
        var routingKey = $"{queue}.#";
        var deadLetterQueue = $"{queue}.dlq";
        var deadLetterExchange = $"{exchange}.dlq";
        var deadLetterRoutingKey = $"{routingKey}.dlq";
        var messageId = Guid.NewGuid().ToString("N");
        var factory = new ConnectionFactory { Uri = new Uri(brokerUri!), AutomaticRecoveryEnabled = false };
        IConnection? connection = null;
        TestManager? manager = null;
        using var cancellation = new CancellationTokenSource();
        Task? consumeTask = null;

        try
        {
            manager = new TestManager(queue, brokerUri!);
            connection = await factory.CreateConnectionAsync();

            var body = Encoding.UTF8.GetBytes("{\"value\":\"one message\"}");
            await using (var publisher = await connection.CreateChannelAsync())
            {
                await publisher.BasicPublishAsync(
                    exchange,
                    routingKey,
                    mandatory: true,
                    new BasicProperties { Persistent = true, MessageId = messageId },
                    body);

                var original = await publisher.BasicGetAsync(queue, autoAck: false);
                Assert.NotNull(original);
                Assert.Equal(messageId, original.BasicProperties.MessageId);
                Assert.False(original.Redelivered);
            }

            await using (var probe = await connection.CreateChannelAsync())
            {
                var redelivered = await probe.BasicGetAsync(queue, autoAck: false);
                Assert.NotNull(redelivered);
                Assert.True(redelivered.Redelivered);
                Assert.Equal(messageId, redelivered.BasicProperties.MessageId);
            }

            var handledIds = new ConcurrentDictionary<string, byte>();
            var observedIds = new ConcurrentQueue<string>();
            var sideEffectCount = 0;
            var duplicateHandled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            consumeTask = manager.ConsumeAsync<TestMessage>((_, deliveredMessageId) =>
            {
                observedIds.Enqueue(deliveredMessageId);
                if (handledIds.TryAdd(deliveredMessageId, 0))
                {
                    Interlocked.Increment(ref sideEffectCount);
                    throw new InvalidOperationException("Simulated lost acknowledgement after commit.");
                }

                duplicateHandled.TrySetResult();
                return Task.CompletedTask;
            }, cancellation.Token);

            await duplicateHandled.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(100);
            cancellation.Cancel();
            await consumeTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, sideEffectCount);
            Assert.Equal(new[] { messageId, messageId }, observedIds.ToArray());
        }
        finally
        {
            cancellation.Cancel();
            if (consumeTask is not null)
            {
                try { await consumeTask.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception) { }
            }

            manager?.Dispose();
            if (connection is not null)
            {
                await using var cleanup = await connection.CreateChannelAsync();
                await cleanup.QueueDeleteAsync(queue);
                await cleanup.QueueDeleteAsync(deadLetterQueue);
                await cleanup.ExchangeDeleteAsync(exchange);
                await cleanup.ExchangeDeleteAsync(deadLetterExchange);
                await connection.DisposeAsync();
            }
        }
    }

    private sealed class TestMessage
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class TestManager : RabbitMQManagerBase<BaseChannelSettings>
    {
        public TestManager(string queue, string brokerUri)
            : base(
                Options.Create(new RabbitMQSettings { Uri = $"plain:{brokerUri}" }),
                Options.Create(new BaseChannelSettings
                {
                    QueueName = queue,
                    ExchangeName = $"{queue}.exchange.topic",
                    RoutingKey = $"{queue}.#",
                    QueueNameDLQ = $"{queue}.dlq",
                    ExchangeNameDLQ = $"{queue}.exchange.topic.dlq",
                    RoutingKeyDLQ = $"{queue}.#.dlq"
                }),
                new AWSKMSKeyProvider(Mock.Of<IAmazonSecretsManager>(), new ConfigurationBuilder().Build()))
        {
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    private sealed class RequiresE2EBrokerFactAttribute : Xunit.FactAttribute
    {
        public RequiresE2EBrokerFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ERP_E2E_RABBITMQ_URI")))
                Skip = "Run scripts/test-e2e-broker-redelivery.py to target the isolated broker.";
        }
    }
}
