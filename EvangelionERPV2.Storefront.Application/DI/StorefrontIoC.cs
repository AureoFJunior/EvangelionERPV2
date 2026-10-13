using EvangelionERPV2.StorefrontModule.Application.Interface;
using EvangelionERPV2.StorefrontModule.Application.Services;
using EvangelionERPV2.StorefrontModule.Application.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EvangelionERPV2.StorefrontModule.Application.DI
{
    public class StorefrontIoC
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<IStorefrontService, StorefrontService>();
            services.AddScoped<IStorefrontCheckoutService, StorefrontCheckoutService>();
            services.AddScoped<IStorefrontCategoryService, StorefrontCategoryService>();
            services.AddScoped<StorefrontPromotionService>();

            // ERP -> Limen e-mails (review spec L4): only when both ends are configured.
            if (Uri.TryCreate(configuration["Storefront:EventsUrl"], UriKind.Absolute, out var eventsUrl) &&
                configuration["Storefront:EventSecret"] is { Length: >= 32 } secret)
            {
                services.AddSingleton(new StorefrontEventSettings(eventsUrl, secret));
                services.AddHttpClient(nameof(StorefrontEventDispatcher), client => client.Timeout = TimeSpan.FromSeconds(10));
                services.AddHostedService<StorefrontEventDispatcher>();
            }
        }
    }
}
