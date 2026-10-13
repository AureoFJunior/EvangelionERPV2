using Amazon.SecretsManager;
using Prometheus;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Worker.NotificationModule.NotificationWorker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EvangelionERPV2.Worker.NotificationModule
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            NotificationLogConfig.Configure();
            new KestrelMetricServer(port: 9101).Start();

            var host = CreateHostBuilder(args).Build();

            using (var scope = host.Services.CreateScope())
                SharedFunctions.Initialize(scope.ServiceProvider);

            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    string envName = "Production";
                    #if DEBUG
                        envName = "Development";
                    #endif
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                          .AddJsonFile($"appsettings.{envName}.json", optional: true, reloadOnChange: true);

                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton<IAmazonSecretsManager, AmazonSecretsManagerClient>(sp =>
                        new AmazonSecretsManagerClient(Amazon.RegionEndpoint.USEast1));
                    services.AddSingleton<AWSKMSKeyProvider>();
                    services.AddSingleton<IDistributedLease, RedisLease>();
                    services.AddHostedService<NotificationSweepWorker>();
                });
    }
}
