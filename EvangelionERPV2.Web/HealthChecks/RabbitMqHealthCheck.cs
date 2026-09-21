using EvangelionERPV2.OrderModule.Application.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EvangelionERPV2.Web.HealthChecks
{
    /// <summary>
    /// Answers whether the API can actually reach RabbitMQ. Publishing is the only thing this host
    /// does with the broker, and until this check a broker that was down was invisible until the
    /// first order tried to enqueue.
    /// </summary>
    public sealed class RabbitMqHealthCheck : IHealthCheck
    {
        /// <summary>Same bound as the other probes.</summary>
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The manager is a singleton that opens its connection <em>in its constructor</em>, so the
        /// first resolution is a blocking connect governed by the driver's own timeout, not ours.
        /// Holding the attempt as one shared task keeps every probe on the same connect instead of
        /// starting a new one each time the broker is down; a faulted attempt is cleared so a later
        /// probe retries.
        /// </summary>
        private static Task<IOrderRabbitMQManager>? _connectAttempt;
        private static readonly Lock ConnectAttemptGate = new();

        private readonly IServiceProvider _serviceProvider;

        public RabbitMqHealthCheck(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var manager = await GetManagerAsync().WaitAsync(ProbeTimeout, cancellationToken);

                return manager.IsConnected
                    ? HealthCheckResult.Healthy("RabbitMQ reachable.")
                    : HealthCheckResult.Unhealthy("RabbitMQ connection is down.");
            }
            catch (TimeoutException)
            {
                return HealthCheckResult.Unhealthy($"RabbitMQ did not connect within {ProbeTimeout.TotalSeconds:0} seconds.");
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Unhealthy($"RabbitMQ probe failed ({exception.GetType().Name}).");
            }
        }

        private Task<IOrderRabbitMQManager> GetManagerAsync()
        {
            lock (ConnectAttemptGate)
            {
                if (_connectAttempt is { IsFaulted: true } or { IsCanceled: true })
                    _connectAttempt = null;

                return _connectAttempt ??= Task.Run(
                    () => _serviceProvider.GetRequiredService<IOrderRabbitMQManager>());
            }
        }
    }
}
