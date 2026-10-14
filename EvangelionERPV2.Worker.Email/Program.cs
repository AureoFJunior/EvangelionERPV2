using Amazon.SecretsManager;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Entities.RabbitMQ;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Worker.EmailModule.EmailWorker.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EvangelionERPV2.Worker.EmailModule.EmailWorker
{

    public class Program
    {
        public static async Task Main(string[] args)
        {
            EmailLogConfig.Configure();
            var host = CreateHostBuilder(args).Build();
            using (var scope = host.Services.CreateScope())
            {
                var serviceProvider = scope.ServiceProvider;
                SharedFunctions.Initialize(serviceProvider);
            }
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
                    {
                        var region = Amazon.RegionEndpoint.USEast1;
                        return new AmazonSecretsManagerClient(region);
                    });
                    services.AddSingleton<AWSKMSKeyProvider>();
                    services.Configure<EmailChannelSettings>(
                        hostContext.Configuration.GetSection("EmailChannelSettings"));
                    services.Configure<RabbitMQSettings>(
                        hostContext.Configuration.GetSection("RabbitMQSettings"));
                    services.AddSingleton(typeof(IRabbitMQManager), typeof(RabbitMQManager));
                    services.AddHostedService<EmailConsumerWorker>();
                    services.AddHostedService<EmailMonthlyOrderSenderWorker>();
                    services.AddHostedService<EmailStockSenderWorker>();
                });
    }
}