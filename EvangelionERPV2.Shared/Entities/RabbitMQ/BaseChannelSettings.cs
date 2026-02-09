using EvangelionERPV2.Shared.Interfaces;
using Microsoft.Extensions.Configuration;

namespace EvangelionERPV2.Shared.Entities.RabbitMQ
{
    public class BaseChannelSettings : IBaseChannelSettings
    {
        public BaseChannelSettings() { }

        public string QueueName { get; set; } = string.Empty;
        public virtual string ExchangeName { get; set; } = string.Empty;
        public virtual string RoutingKey { get; set; } = string.Empty;
        public virtual string QueueNameDLQ { get; set; } = string.Empty;
        public virtual string ExchangeNameDLQ { get; set; } = string.Empty;
        public virtual string RoutingKeyDLQ { get; set; } = string.Empty;

    }
}
