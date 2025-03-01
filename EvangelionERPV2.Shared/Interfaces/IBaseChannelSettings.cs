namespace EvangelionERPV2.Shared.Interfaces
{
    public interface IBaseChannelSettings
    {
        public string QueueName { get; set; }
        public string ExchangeName { get; set; }
        public string RoutingKey { get; set; }
        public string QueueNameDLQ { get; set; }
        public string ExchangeNameDLQ { get; set; }
        public string RoutingKeyDLQ { get; set; }
    }
}
