using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;

namespace EvangelionERPV2.Web.HealthChecks
{
    /// <summary>
    /// Answers whether the API can actually reach SQL Server. Without it <c>/health</c> reported
    /// healthy with the database down, so every probe reading it was measuring the process, not the
    /// service.
    /// </summary>
    public sealed class DatabaseHealthCheck : IHealthCheck
    {
        /// <summary>
        /// Bounded on purpose: a probe that hangs on a dead connection is as useless as one that
        /// always says healthy, and the orchestrator has its own, longer timeout.
        /// </summary>
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The count this check already has in hand, exported so it can be alerted on. A non-zero
        /// value explains a wave of 500s faster than any latency panel, and the probe recomputes it
        /// every time it runs — so unlike the startup log, this stays true after somebody applies
        /// them without restarting anything.
        /// </summary>
        private static readonly Gauge PendingMigrations = Metrics.CreateGauge(
            "evangelion_pending_migrations",
            "Migrations committed to the branch but not applied to this database");

        private readonly AppDbContext _context;

        public DatabaseHealthCheck(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Nothing in the app calls <c>Database.Migrate()</c> - by decision - and until this check
        /// nothing said so either. A committed migration that nobody applied turns every request
        /// touching the new tables into a 500 with <c>Invalid column name</c>, and diagnosing that
        /// from the call site has cost two separate days here.
        /// <para>
        /// Deliberately <see cref="HealthStatus.Degraded"/> rather than Unhealthy: production has
        /// never had these migrations applied, so failing the probe would refuse to bring the API
        /// up at all. Degraded still answers 200, and names the ids in the body.
        /// </para>
        /// </summary>
        public static HealthCheckResult DescribePendingMigrations(IReadOnlyCollection<string> pendingMigrations)
        {
            if (pendingMigrations.Count == 0)
                return HealthCheckResult.Healthy("Database reachable, schema up to date.");

            return HealthCheckResult.Degraded(
                $"Database reachable, but {pendingMigrations.Count} migration(s) have not been applied: " +
                string.Join(", ", pendingMigrations));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            try
            {
                if (!await _context.Database.CanConnectAsync(timeout.Token))
                    return HealthCheckResult.Unhealthy("Database is not reachable.");

                var pending = (await _context.Database.GetPendingMigrationsAsync(timeout.Token)).ToList();
                PendingMigrations.Set(pending.Count);
                return DescribePendingMigrations(pending);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Unhealthy($"Database did not answer within {ProbeTimeout.TotalSeconds:0} seconds.");
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Unhealthy($"Database probe failed ({exception.GetType().Name}).");
            }
        }
    }
}
