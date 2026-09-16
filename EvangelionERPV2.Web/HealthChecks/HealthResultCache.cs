using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EvangelionERPV2.Web.HealthChecks
{
    /// <summary>
    /// Runs the health checks at most once every <see cref="Ttl"/>, however often <c>/health</c> is
    /// asked.
    /// <para>
    /// <c>/health</c> is anonymous and, since the overload limiter was added, deliberately exempt
    /// from it - an orchestrator that cannot reach health during overload will kill a container that
    /// is successfully defending itself. That exemption is right, but it left an unauthenticated
    /// path that did real work on every call: a SQL connect plus a migrations-history read, a Redis
    /// round trip and a broker probe, measured at about 150 ms each. The per-IP limiter caps a
    /// single source at 60 a minute in production; it does not cap many sources, and the work is
    /// global while the limit is per-caller.
    /// </para>
    /// <para>
    /// Caching turns that into a fixed cost: one execution per TTL no matter the request rate. The
    /// single-flight lock matters as much as the TTL - without it, a burst arriving on a cold cache
    /// would all miss together and every one of them would run the checks.
    /// </para>
    /// </summary>
    public sealed class HealthResultCache : IDisposable
    {
        /// <summary>
        /// Short enough that a dependency failing is still noticed promptly - the compose and ECS
        /// probes poll on a 30 s interval, so five seconds costs nothing in detection time.
        /// </summary>
        public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

        private readonly HealthCheckService _healthCheckService;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private HealthReport? _report;

        // Ticks in a long rather than a DateTime: Volatile/Interlocked give an atomic 64-bit read
        // and write, which a DateTime field read outside the lock does not guarantee.
        private long _expiresAtUtcTicks;

        public HealthResultCache(HealthCheckService healthCheckService)
        {
            _healthCheckService = healthCheckService;
        }

        public async Task<HealthReport> GetAsync(CancellationToken cancellationToken)
        {
            var cached = Volatile.Read(ref _report);
            if (cached != null && DateTime.UtcNow.Ticks < Interlocked.Read(ref _expiresAtUtcTicks))
                return cached;

            await _lock.WaitAsync(cancellationToken);
            try
            {
                // Re-check inside the lock: everyone queued behind a cold cache should use the one
                // result the first of them produced, not run the checks again in turn.
                cached = Volatile.Read(ref _report);
                if (cached != null && DateTime.UtcNow.Ticks < Interlocked.Read(ref _expiresAtUtcTicks))
                    return cached;

                var report = await _healthCheckService.CheckHealthAsync(cancellationToken);

                Volatile.Write(ref _report, report);
                Interlocked.Exchange(ref _expiresAtUtcTicks, DateTime.UtcNow.Add(Ttl).Ticks);

                return report;
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Dispose() => _lock.Dispose();
    }
}
