using EvangelionERPV2.OrderModule.Application.DI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EvangelionERPV2.Test.HealthChecks
{
    public class SharedIoCCacheRegistrationTests
    {
        /// <summary>
        /// Product, Order and Reports each used to register their own identical Redis cache; only
        /// the last one to run survived resolution. Now SharedIoC owns the single registration, and
        /// this fails if a module quietly grows its own copy again - or if the last one is deleted.
        /// </summary>
        [Fact]
        public void Configure_RegistersExactlyOneDistributedCache()
        {
            var services = new ServiceCollection();

            SharedIoC.Configure(services, new ConfigurationBuilder().Build());

            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDistributedCache));
        }
    }
}
