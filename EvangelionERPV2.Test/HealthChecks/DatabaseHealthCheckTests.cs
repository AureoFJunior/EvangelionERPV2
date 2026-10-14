using EvangelionERPV2.Web.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EvangelionERPV2.Test.HealthChecks
{
    public class DatabaseHealthCheckTests
    {
        [Fact]
        public void DescribePendingMigrations_NoPendingMigrations_ReportsHealthy()
        {
            var result = DatabaseHealthCheck.DescribePendingMigrations([]);

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }

        [Fact]
        public void DescribePendingMigrations_PendingMigrations_ReportsDegradedAndNamesThem()
        {
            var result = DatabaseHealthCheck.DescribePendingMigrations(
                ["20260826174529_AddPdvOverrideAudit", "20260825120000_AddCustomerCoordinates"]);

            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Contains("2 migration(s)", result.Description);
            Assert.Contains("20260826174529_AddPdvOverrideAudit", result.Description);
            Assert.Contains("20260825120000_AddCustomerCoordinates", result.Description);
        }
    }
}
