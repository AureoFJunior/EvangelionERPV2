using EvangelionERPV2.Web.HealthChecks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;

namespace EvangelionERPV2.Test.HealthChecks
{
    public class RedisHealthCheckTests
    {
        [Fact]
        public async Task CheckHealthAsync_CacheAnswers_ReportsHealthy()
        {
            var cache = new Mock<IDistributedCache>();
            cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[]?)null);

            var result = await new RedisHealthCheck(cache.Object)
                .CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }

        [Fact]
        public async Task CheckHealthAsync_CacheThrows_ReportsUnhealthyWithoutLeakingTheConnectionString()
        {
            var cache = new Mock<IDistributedCache>();
            cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis://user:hunter2@cache.internal:6379"));

            var result = await new RedisHealthCheck(cache.Object)
                .CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.DoesNotContain("hunter2", result.Description);
            Assert.DoesNotContain("cache.internal", result.Description);
        }
    }
}
