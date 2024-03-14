using Microsoft.Extensions.Configuration;
using System;

namespace EvangelionERPV2.Domain.Models.RabbitMQ
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
        }

        public OrderChannelSettings() { }

        public new string QueueName { get; set; }
        public new string ExchangeName => $"order.{QueueName}.exchange.topic";
        public new string RoutingKey => $"order.{QueueName}.#";
        public new string QueueNameDLQ => $"{QueueName}.dlq";
        public new string ExchangeNameDLQ => $"{ExchangeName}.dlq";
        public new string RoutingKeyDLQ => $"{RoutingKey}.dlq";

    }
}
