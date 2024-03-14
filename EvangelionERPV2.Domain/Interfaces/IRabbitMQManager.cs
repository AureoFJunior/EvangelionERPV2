using EvangelionERPV2.Domain.Models.RabbitMQ;
using RabbitMQ.Client;

namespace EvangelionERPV2.Domain.Interfaces
{
    public interface IRabbitMQManager
    {
        void DeclareQueue(BaseChannelSettings channelSettings, bool durable = true, bool exclusive = false, bool autoDelete = false);
        Task<T> Dequeue<T>(BaseChannelSettings channelSettings);
        Task<T> DequeueAndProcess<T>(BaseChannelSettings channelSettings);
        Task<T> DequeueAndProcessAsync<T>(BaseChannelSettings channelSettings);
        Task<T> DequeueAsync<T>(BaseChannelSettings channelSettings);
        Task<IEnumerable<T>> DequeueList<T>(BaseChannelSettings channelSettings);
        Task<IEnumerable<T>> DequeueListAsync<T>(BaseChannelSettings channelSettings);
        void Enqueue<T>(IEnumerable<T> obj, BaseChannelSettings channelSettings);
        void Enqueue<T>(T obj, BaseChannelSettings channelSettings);
        Task EnqueueAsync<T>(IEnumerable<T> obj, BaseChannelSettings channelSettings);
        Task EnqueueAsync<T>(T obj, BaseChannelSettings channelSettings);
        IModel? GetChannel(BaseChannelSettings channelSettings);
    }
}
