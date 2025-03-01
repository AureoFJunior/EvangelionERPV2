using EvangelionERPV2.Shared.Interfaces;
using Microsoft.Extensions.Configuration;
using System;

namespace EvangelionERPV2.Shared.Entities.RabbitMQ
{
    public class BaseChannelSettings : IBaseChannelSettings
    {
        public BaseChannelSettings() { }

        public string QueueName { get; set; }
        public virtual string ExchangeName { get; set; }
        public virtual string RoutingKey { get; set; }
        public virtual string QueueNameDLQ { get; set; }
        public virtual string ExchangeNameDLQ { get; set; }
        public virtual string RoutingKeyDLQ { get; set; }

    }
}
