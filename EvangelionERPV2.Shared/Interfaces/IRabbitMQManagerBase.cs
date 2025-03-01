using EvangelionERPV2.Shared.Entities.RabbitMQ;

namespace EvangelionERPV2.Shared.Interfaces
{
    public interface IRabbitMQManagerBase
    {
        Task<T> DequeueAndProcessAsync<T>();
        Task EnqueueAsync<T>(T obj);
        void SetupChannel();
    }
}
