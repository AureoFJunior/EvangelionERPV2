using Amazon.SecretsManager;
using EvangelionERPV2.ReportsModule.Application.Interface;
using EvangelionERPV2.ReportsModule.Application.Services;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvangelionERPV2.ReportsModule.Application.DI
{
    public static class ReportsIoC
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            try
            {
                services.AddSingleton<IAmazonSecretsManager, AmazonSecretsManagerClient>(sp =>
                {
                    var region = Amazon.RegionEndpoint.USEast1;
                    return new AmazonSecretsManagerClient(region);
                });
                services.AddSingleton<AWSKMSKeyProvider>();

                var serviceProvider = services.BuildServiceProvider();
                var kmsProvider = serviceProvider.GetRequiredService<AWSKMSKeyProvider>();

                services.AddStackExchangeRedisCache(o =>
                {
                    o.InstanceName = kmsProvider.GetKMSKey(configuration.GetSection("RedisSettings")["InstanceName"] ?? string.Empty);
                    o.Configuration = kmsProvider.GetKMSKey(configuration.GetSection("RedisSettings")["ConnectionString"] ?? string.Empty);
                });

                services.AddScoped<IReportsService, ReportsService>();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Error at DI IoC Reports: {Message}", ex.Message);
            }
        }
    }
}
