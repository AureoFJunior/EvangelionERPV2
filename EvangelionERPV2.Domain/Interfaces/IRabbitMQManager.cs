using RabbitMQ.Client;

namespace EvangelionERPV2.Domain.Interfaces
{
    public interface IRabbitMQManager
    {
        Task<T> Dequeue<T>(string queueName);
        Task<T> DequeueAndProcess<T>(string queueName);
        Task<T> DequeueAndProcessAsync<T>(string queueName);
        Task<T> DequeueAsync<T>(string queueName);
        Task<IEnumerable<T>> DequeueList<T>(string queueName);
        Task<IEnumerable<T>> DequeueListAsync<T>(string queueName);
        void Enqueue<T>(IEnumerable<T> obj, string queueName);
        void Enqueue<T>(T obj, string queueName);
        Task EnqueueAsync<T>(IEnumerable<T> obj, string queueName);
        Task EnqueueAsync<T>(T obj, string queueName);
        IModel? GetChannel(string queueName);
    }
}
