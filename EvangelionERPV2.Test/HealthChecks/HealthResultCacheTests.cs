using EvangelionERPV2.Web.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EvangelionERPV2.Test.HealthChecks
{
    /// <summary>
    /// /health is anonymous and exempt from the overload limiter, so the work behind it has to be
    /// bounded by time rather than by request count.
    /// </summary>
    public class HealthResultCacheTests
    {
        private sealed class CountingCheck : IHealthCheck
        {
            public int Executions;

            public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Executions);
                return Task.FromResult(HealthCheckResult.Healthy());
            }
        }

        private static (HealthResultCache Cache, CountingCheck Check) Build()
        {
            var check = new CountingCheck();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHealthChecks().AddCheck("counting", check);

            var provider = services.BuildServiceProvider();
            return (new HealthResultCache(provider.GetRequiredService<HealthCheckService>()), check);
        }

        [Fact]
        public async Task ManyCalls_RunTheChecksOnce()
        {
            var (cache, check) = Build();

            for (var i = 0; i < 50; i++)
                await cache.GetAsync(CancellationToken.None);

            Assert.Equal(1, check.Executions);
        }

        /// <summary>
        /// The single-flight lock, not just the TTL: a burst arriving on a cold cache would
        /// otherwise all miss together and every one of them would run the checks - which is the
        /// amplification this cache exists to prevent.
        /// </summary>
        [Fact]
        public async Task AConcurrentBurstOnAColdCache_RunsTheChecksOnce()
        {
            var (cache, check) = Build();

            await Task.WhenAll(Enumerable.Range(0, 100)
                .Select(_ => cache.GetAsync(CancellationToken.None)));

            Assert.Equal(1, check.Executions);
        }

        [Fact]
        public async Task TheResultIsStillTheRealOne()
        {
            var (cache, _) = Build();

            var report = await cache.GetAsync(CancellationToken.None);

            Assert.Equal(HealthStatus.Healthy, report.Status);
            Assert.True(report.Entries.ContainsKey("counting"));
        }
    }
}
