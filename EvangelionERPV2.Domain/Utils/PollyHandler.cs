using Polly;
using Polly.Retry;

namespace EvangelionERPV2.Domain.Utils
{
    public class PollyHandler
    {
        public RetryPolicy RequestPolly;
        public AsyncRetryPolicy AsyncTestePolicy;

        public PollyHandler()
        {
            RequestPolly = Policy.Handle<Exception>().WaitAndRetry(3, retry => TimeSpan.FromSeconds(1),
                (ex, timestamp) =>
                {
                    Console.WriteLine("Retrying...");
                });
        }
    }
}
