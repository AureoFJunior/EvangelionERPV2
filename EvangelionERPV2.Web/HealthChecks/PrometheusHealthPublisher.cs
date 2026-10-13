using Microsoft.Extensions.Diagnostics.HealthChecks;
using Prometheus;

namespace EvangelionERPV2.Web.HealthChecks
{
    /// <summary>
    /// Turns the health checks into series so an alert can fire on them. Without this, knowing that
    /// SQL Server is down means somebody deciding to call <c>/health</c> — which is the one thing
    /// nobody does while an incident is starting.
    /// <para>
    /// <see cref="IHealthCheckPublisher"/> is the framework's own hook: registering it is enough,
    /// the host calls it on a timer with the full report. No hosted service of our own, no second
    /// place that knows the interval.
    /// </para>
    /// </summary>
    public sealed class PrometheusHealthPublisher : IHealthCheckPublisher
    {
        private static readonly Gauge Status = Metrics.CreateGauge(
            "evangelion_health_check_status",
            "Health of one dependency: 1 healthy, 0.5 degraded, 0 unhealthy",
            new GaugeConfiguration { LabelNames = ["check"] });

        /// <summary>
        /// Deliberately a number and not a label: an alert reads <c>&lt; 1</c> and catches Degraded
        /// as well as Unhealthy, which matters because the database check reports Degraded — not
        /// Unhealthy — for pending migrations, and that is a condition worth waking up for.
        /// </summary>
        public static double ToGaugeValue(HealthStatus status) => status switch
        {
            HealthStatus.Healthy => 1,
            HealthStatus.Degraded => 0.5,
            _ => 0
        };

        public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
        {
            foreach (var entry in report.Entries)
                Status.WithLabels(entry.Key).Set(ToGaugeValue(entry.Value.Status));

            return Task.CompletedTask;
        }
    }
}
