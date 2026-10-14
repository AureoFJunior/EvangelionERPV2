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
                var region = Amazon.RegionEndpoint.USEast1;
                var secretsManagerClient = new AmazonSecretsManagerClient(region);
                services.AddSingleton<IAmazonSecretsManager>(secretsManagerClient);

                var kmsProvider = new AWSKMSKeyProvider(secretsManagerClient, configuration);
                services.AddSingleton(kmsProvider);

                services.AddStackExchangeRedisCache(o =>
                {
                    o.InstanceName = kmsProvider.GetKMSKey(configuration.GetSection("RedisSettings")["InstanceName"] ?? string.Empty);
                    o.Configuration = kmsProvider.GetKMSKey(configuration.GetSection("RedisSettings")["ConnectionString"] ?? string.Empty);
                });

                services.AddScoped<IReportsService, ReportsService>();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error at DI IoC Reports. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }
    }
}
