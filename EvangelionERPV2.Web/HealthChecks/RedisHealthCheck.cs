using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EvangelionERPV2.Web.HealthChecks
{
    /// <summary>
    /// Answers whether the API can actually reach Redis. It is load-bearing twice over — the
    /// product and order read caches, and the <c>RedisLease</c> that keeps the scheduled sweeps to
    /// one runner across replicas — and nothing used to probe it.
    /// <para>
    /// It also forces the one thing a config file cannot show: the connection string is a Secrets
    /// Manager reference resolved *inside* the cache options callback, so a Secrets Manager outage
    /// used to surface the first time something touched the cache, never at boot.
    /// </para>
    /// </summary>
    public sealed class RedisHealthCheck : IHealthCheck
    {
        /// <summary>Same bound as the database probe: a hung probe is as useless as a lying one.</summary>
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// A read, not a write: the probe must not leave anything behind in a shared cache, and a
        /// miss proves the round trip just as well as a hit.
        /// </summary>
        private const string ProbeKey = "healthcheck:ping";

        private readonly IDistributedCache _cache;

        public RedisHealthCheck(IDistributedCache cache)
        {
            _cache = cache;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            try
            {
                await _cache.GetAsync(ProbeKey, timeout.Token);
                return HealthCheckResult.Healthy("Redis reachable.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Unhealthy($"Redis did not answer within {ProbeTimeout.TotalSeconds:0} seconds.");
            }
            catch (Exception exception)
            {
                // The message is not echoed: a Redis connection failure carries the host and the
                // password, and /health is anonymous.
                return HealthCheckResult.Unhealthy($"Redis probe failed ({exception.GetType().Name}).");
            }
        }
    }
}
