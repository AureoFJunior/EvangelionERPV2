using AspNetCoreRateLimit;
using EvangelionERPV2.Web.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Storefront.Api calls as one service account from one address and fans in every shopper, so that
    /// address gets its own quota on the storefront endpoints (storefront spec §4.4) — and nothing more:
    /// its login and every other endpoint keep the production limits.
    /// </summary>
    public class StorefrontRateLimitTests
    {
        private const string StorefrontAddress = "10.20.0.7";

        private static readonly IpRateLimitOptions Production = LoadProductionOptions();

        private static IpRateLimitOptions LoadProductionOptions()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(dir.FullName, "EvangelionERPV2.sln"))) dir = dir.Parent!;
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(dir.FullName, "EvangelionERPV2.Web", "appsettings.Production.json"))
                .Build();
            return configuration.GetSection("IpRateLimiting").Get<IpRateLimitOptions>()!;
        }

        private static async Task<long> PerMinute(Dictionary<string, string?> settings, string ip, string path)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var policies = new IpRateLimitPolicies();
            StorefrontRateLimit.Apply(policies, configuration);

            var store = new MemoryCacheIpPolicyStore(new MemoryCache(new MemoryCacheOptions()), Options.Create(Production), Options.Create(policies));
            await store.SeedAsync();
            var processor = new IpRateLimitProcessor(Production, store, null!);

            var rules = await processor.GetMatchingRulesAsync(new ClientRequestIdentity { ClientIp = ip, Path = path.ToLowerInvariant(), HttpVerb = "get" });
            return (long)rules.Single(r => r.Period == "1m").Limit;
        }

        private static readonly Dictionary<string, string?> Configured = new() { ["Storefront:ApiAddress"] = StorefrontAddress };

        [Fact]
        public async Task The_storefront_address_gets_its_own_quota_on_storefront_endpoints()
        {
            Assert.Equal(StorefrontRateLimit.DefaultRequestsPerMinute, await PerMinute(Configured, StorefrontAddress, "/api/v1/Storefront/Products/1/24"));

            var tuned = new Dictionary<string, string?>(Configured) { ["Storefront:RequestsPerMinute"] = "3000" };
            Assert.Equal(3000, await PerMinute(tuned, StorefrontAddress, "/api/v1/Storefront/Product/7d1c4b0e-0000-4000-8000-000000000001"));
        }

        [Fact]
        public async Task Its_login_and_every_other_endpoint_keep_the_production_limits()
        {
            Assert.Equal(5, await PerMinute(Configured, StorefrontAddress, "/api/v1/Storefront/Login"));
            Assert.Equal(60, await PerMinute(Configured, StorefrontAddress, "/api/v1/Order/GetOrders"));
            Assert.Equal(5, await PerMinute(Configured, StorefrontAddress, "/api/v1/User/LogInto"));
        }

        [Fact]
        public async Task Any_other_address_or_no_setting_keeps_the_general_limit()
        {
            Assert.Equal(60, await PerMinute(Configured, "203.0.113.9", "/api/v1/Storefront/Products/1/24"));
            Assert.Equal(60, await PerMinute([], StorefrontAddress, "/api/v1/Storefront/Products/1/24"));
        }

        /// <summary>The wiring Program uses: without seeding at start-up the store stays empty and no IP policy applies.</summary>
        [Fact]
        public async Task At_start_up_the_storefront_policy_reaches_the_store_the_middleware_reads()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(Configured).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            StorefrontRateLimit.AddIpRateLimiting(services, configuration);
            await using var provider = services.BuildServiceProvider();

            foreach (var hosted in provider.GetServices<IHostedService>()) await hosted.StartAsync(CancellationToken.None);

            var prefix = provider.GetRequiredService<IOptions<IpRateLimitOptions>>().Value.IpPolicyPrefix;
            var policies = await provider.GetRequiredService<IIpPolicyStore>().GetAsync(prefix);
            Assert.Contains(policies.IpRules, rule => rule.Ip == StorefrontAddress);
        }
    }
}