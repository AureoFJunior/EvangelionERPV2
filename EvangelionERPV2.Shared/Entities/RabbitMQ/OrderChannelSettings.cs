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
            QueueName = configurationSection["QueueName"];
            BatchSize = Convert.ToUInt32(configurationSection["BatchSize"]);
        }

        public OrderChannelSettings() { }

        public new string QueueName { get; set; }
        public uint BatchSize { get; set; }
        public override string ExchangeName => $"order.{QueueName}.exchange.topic";
        public override string RoutingKey => $"order.{QueueName}.#";
        public override string QueueNameDLQ => $"{QueueName}.dlq";
        public override string ExchangeNameDLQ => $"{ExchangeName}.dlq";
        public override string RoutingKeyDLQ => $"{RoutingKey}.dlq";

    }
}
