using EvangelionERPV2.Web.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EvangelionERPV2.Test.HealthChecks
{
    public class PrometheusHealthPublisherTests
    {
        [Fact]
        public void ToGaugeValue_Healthy_IsOne()
        {
            Assert.Equal(1, PrometheusHealthPublisher.ToGaugeValue(HealthStatus.Healthy));
        }

        [Fact]
        public void ToGaugeValue_Degraded_IsBetween()
        {
            var degraded = PrometheusHealthPublisher.ToGaugeValue(HealthStatus.Degraded);

            Assert.True(degraded < PrometheusHealthPublisher.ToGaugeValue(HealthStatus.Healthy));
            Assert.True(degraded > PrometheusHealthPublisher.ToGaugeValue(HealthStatus.Unhealthy));
        }

        [Fact]
        public void ToGaugeValue_Unhealthy_IsZero()
        {
            Assert.Equal(0, PrometheusHealthPublisher.ToGaugeValue(HealthStatus.Unhealthy));
        }

        [Fact]
        public async Task PublishAsync_WritesOneSeriesPerCheck()
        {
            var report = new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    ["sqlserver"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null),
                    ["redis"] = new(HealthStatus.Unhealthy, null, TimeSpan.Zero, null, null)
                },
                TimeSpan.Zero);

            await new PrometheusHealthPublisher().PublishAsync(report, CancellationToken.None);

            var scrape = await Scrape();

            Assert.Contains("evangelion_health_check_status{check=\"sqlserver\"} 1", scrape);
            Assert.Contains("evangelion_health_check_status{check=\"redis\"} 0", scrape);
        }

        private static async Task<string> Scrape()
        {
            using var buffer = new MemoryStream();
            await global::Prometheus.Metrics.DefaultRegistry.CollectAndExportAsTextAsync(buffer);
            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
