using Microsoft.Extensions.Configuration;
using System;

namespace EvangelionERPV2.Domain.Models.RabbitMQ
{
    public class EmailChannelSettings : BaseChannelSettings
    {
        public EmailChannelSettings(string queueName)
        {
            QueueName = queueName;
        }

        public EmailChannelSettings(IConfigurationSection configurationSection)
        {
            QueueName = configurationSection["QueueName"];
        }

        public EmailChannelSettings() { }

        public new string QueueName { get; set; }
        public new string ExchangeName => $"email.{QueueName}.exchange.topic";
        public new string RoutingKey => $"email.{QueueName}.#";
        public new string QueueNameDLQ => $"{QueueName}.dlq";
        public new string ExchangeNameDLQ => $"{ExchangeName}.dlq";
        public new string RoutingKeyDLQ => $"{RoutingKey}.dlq";

    }
}
