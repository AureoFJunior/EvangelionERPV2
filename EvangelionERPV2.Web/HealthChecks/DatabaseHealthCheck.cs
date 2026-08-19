using EvangelionERPV2.Shared.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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

        private readonly AppDbContext _context;

        public DatabaseHealthCheck(AppDbContext context)
        {
            _context = context;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            try
            {
                return await _context.Database.CanConnectAsync(timeout.Token)
                    ? HealthCheckResult.Healthy("Database reachable.")
                    : HealthCheckResult.Unhealthy("Database is not reachable.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return HealthCheckResult.Unhealthy($"Database did not answer within {ProbeTimeout.TotalSeconds:0} seconds.");
            }
            catch (Exception exception)
            {
                // The message is not echoed: a connection failure carries the server name and the
                // login, and /health is anonymous.
                return HealthCheckResult.Unhealthy($"Database probe failed ({exception.GetType().Name}).");
            }
        }
    }
}
