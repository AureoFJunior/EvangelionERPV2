using Microsoft.Extensions.Configuration;
using System;

namespace EvangelionERPV2.Shared.Entities.RabbitMQ
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
        public override string ExchangeName => $"email.{QueueName}.exchange.topic";
        public override string RoutingKey => $"email.{QueueName}.#";
        public override string QueueNameDLQ => $"{QueueName}.dlq";
        public override string ExchangeNameDLQ => $"{ExchangeName}.dlq";
        public override string RoutingKeyDLQ => $"{RoutingKey}.dlq";

    }
}
