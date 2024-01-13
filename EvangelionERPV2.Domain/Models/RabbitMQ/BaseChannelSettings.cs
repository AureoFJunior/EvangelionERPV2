using Microsoft.Extensions.Configuration;
using System;

namespace EvangelionERPV2.Domain.Models.RabbitMQ
{
    public class BaseChannelSettings
    {
        public BaseChannelSettings() { }

        public string QueueName { get; set; }
        public string ExchangeName { get; set; }
        public string RoutingKey { get; set; }
        public string QueueNameDLQ { get; set; }
        public string ExchangeNameDLQ { get; set; }
        public string RoutingKeyDLQ { get; set; }

    }
}
