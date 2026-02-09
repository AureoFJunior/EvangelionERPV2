using Microsoft.Extensions.Configuration;
using System;

namespace EvangelionERPV2.Shared.Entities.RabbitMQ
{
    public class OrderChannelSettings : BaseChannelSettings
    {
        public OrderChannelSettings(string queueName)
        {
            QueueName = queueName;
        }

        public OrderChannelSettings(IConfigurationSection configurationSection)
        {
            QueueName = configurationSection["QueueName"] ?? string.Empty;
            BatchSize = uint.TryParse(configurationSection["BatchSize"], out var batchSize) ? batchSize : 0;
        }

        public OrderChannelSettings() { }

        public new string QueueName { get; set; } = string.Empty;
        public uint BatchSize { get; set; }
        public override string ExchangeName => $"order.{QueueName}.exchange.topic";
        public override string RoutingKey => $"order.{QueueName}.#";
        public override string QueueNameDLQ => $"{QueueName}.dlq";
        public override string ExchangeNameDLQ => $"{ExchangeName}.dlq";
        public override string RoutingKeyDLQ => $"{RoutingKey}.dlq";

    }
}
