using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
using EvangelionERPV2.Shared.Configs;
using Amazon.SecretsManager;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Caching.StackExchangeRedis;


namespace EvangelionERPV2.OrderModule.Application.DI
{
    public class SharedIoC
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            try
            {
                #region AWS
                services.AddSingleton<IAmazonSecretsManager, AmazonSecretsManagerClient>(sp =>
                {
                    var region = Amazon.RegionEndpoint.USEast1;
                    return new AmazonSecretsManagerClient(region);
                });
                services.AddSingleton<AWSKMSKeyProvider>();
                #endregion

                // Singleton so every hosted service in this host shares one Redis connection.
                services.AddSingleton<IDistributedLease, RedisLease>();

                // Prunes the idempotency table. Leased, so running in every host costs one sweep
                // across the cluster rather than one per replica.
                services.AddHostedService<ProcessedMessageRetentionWorker>();

                #region Redis
                // The one registration for the whole host. Product, Order and Reports each carried
                // an identical copy of this; only the last one to run survived resolution, so the
                // other two read like configuration while doing nothing.
                services.AddStackExchangeRedisCache(_ => { });
                services.AddOptions<RedisCacheOptions>()
                    .Configure<AWSKMSKeyProvider>((options, kmsProvider) =>
                    {
                        options.InstanceName = kmsProvider.GetKMSKey(configuration.GetSection("RedisSettings")["InstanceName"] ?? string.Empty);
                        options.Configuration = kmsProvider.GetKMSKey(configuration.GetSection("RedisSettings")["ConnectionString"] ?? string.Empty);
                    });
                #endregion

                services.AddLogging();

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(config => { }, AppDomain.CurrentDomain.GetAssemblies());

                #endregion

                #region SingnalR
                services.AddSignalR();
                #endregion
            }
            catch (Exception ex)
            {
                Log.Logger.Error(
                    "Error at DI IoC Shared. ErrorType={ErrorType}",
                    ex.GetType().Name);
            }

        }
    }
}
