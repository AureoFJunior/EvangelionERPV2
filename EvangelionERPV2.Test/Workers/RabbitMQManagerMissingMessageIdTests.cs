using System.Reflection;
using System.Runtime.CompilerServices;
using EvangelionERPV2.Shared.Entities.RabbitMQ;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EvangelionERPV2.Test.Workers;

public sealed class RabbitMQManagerMissingMessageIdTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task DeliveryWithoutMessageId_IsDeadLetteredWithoutCallingHandler(string? messageId)
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        channel
            .Setup(x => x.BasicNackAsync(17, false, false, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var manager = (TestRabbitMQManager)RuntimeHelpers.GetUninitializedObject(typeof(TestRabbitMQManager));
        SetField(manager, "_channel", channel.Object);
        SetField(manager, "_channelLock", new SemaphoreSlim(1, 1));
        SetField(manager, "_channelSettings", new BaseChannelSettings { QueueName = "test" });

        var properties = new BasicProperties { MessageId = messageId };
        var delivery = new BasicDeliverEventArgs(
            "consumer",
            17,
            false,
            "exchange",
            "routing-key",
            properties,
            ReadOnlyMemory<byte>.Empty,
            CancellationToken.None);
        var handlerCalled = false;
        Func<string, string, Task> handler = (_, _) =>
        {
            handlerCalled = true;
            return Task.CompletedTask;
        };

        var method = typeof(RabbitMQManagerBase<BaseChannelSettings>).GetMethod(
            "HandleDeliveryAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(string));

        await (Task)method.Invoke(manager, [delivery, handler])!;

        Assert.False(handlerCalled);
        channel.Verify(x => x.BasicNackAsync(17, false, false, It.IsAny<CancellationToken>()), Times.Once);
        channel.VerifyNoOtherCalls();
    }

    private static void SetField(object target, string name, object value)
    {
        var field = typeof(RabbitMQManagerBase<BaseChannelSettings>).GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private sealed class TestRabbitMQManager : RabbitMQManagerBase<BaseChannelSettings>
    {
        public TestRabbitMQManager()
            : base(Options.Create(new RabbitMQSettings()), Options.Create(new BaseChannelSettings()), null!)
        {
        }
    }
}
