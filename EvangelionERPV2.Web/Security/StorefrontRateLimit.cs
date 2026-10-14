using AspNetCoreRateLimit;

namespace EvangelionERPV2.Web.Security
{
    /// <summary>
    /// Storefront.Api calls as one service account from one address and fans in every shopper, so the
    /// general per-address limit would throttle the whole marketplace. <c>Storefront:ApiAddress</c> (as
    /// nginx sees it in X-Real-IP) gets <c>Storefront:RequestsPerMinute</c> on the storefront endpoints;
    /// its login keeps the login limit, and every other endpoint the general rules.
    /// </summary>
    public static class StorefrontRateLimit
    {
        public const int DefaultRequestsPerMinute = 1200;
        public const int LoginPerMinute = 5;

        /// <summary>The ERP's per-address limits (AspNetCoreRateLimit), with the storefront's quota added.</summary>
        public static void AddIpRateLimiting(IServiceCollection services, IConfiguration configuration)
        {
            services.AddMemoryCache();
            services.AddHttpContextAccessor();
            services.Configure<IpRateLimitOptions>(configuration.GetSection("IpRateLimiting"));
            services.Configure<IpRateLimitPolicies>(configuration.GetSection("IpRateLimitPolicies"));
            services.PostConfigure<IpRateLimitPolicies>(policies => Apply(policies, configuration));
            services.AddInMemoryRateLimiting();
            services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
            services.AddHostedService<IpPolicySeeder>();
        }

        /// <summary>AspNetCoreRateLimit reads IP policies only from its store, which starts empty until seeded.</summary>
        private sealed class IpPolicySeeder(IIpPolicyStore store) : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken) => store.SeedAsync();

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        public static void Apply(IpRateLimitPolicies policies, IConfiguration configuration)
        {
            if (configuration["Storefront:ApiAddress"] is not { Length: > 0 } address)
                return;

            var perMinute = configuration.GetValue<int?>("Storefront:RequestsPerMinute") ?? DefaultRequestsPerMinute;
            policies.IpRules ??= new List<IpRateLimitPolicy>();
            policies.IpRules.Add(new IpRateLimitPolicy
            {
                Ip = address,
                Rules = new List<RateLimitRule>
                {
                    new() { Endpoint = "*:/api/v1/storefront/*", Period = "1m", Limit = perMinute },
                    new() { Endpoint = "*:/api/v1/storefront/login", Period = "1m", Limit = LoginPerMinute }
                }
            });
        }
    }
}
