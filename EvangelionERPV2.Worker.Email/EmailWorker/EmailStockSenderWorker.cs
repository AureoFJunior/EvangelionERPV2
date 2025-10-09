using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EvangelionERPV2.Worker.EmailModule.EmailWorker
{
    public sealed class EmailStockSenderWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        private AWSKMSKeyProvider _kmsProvider;

        public EmailStockSenderWorker(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var polly = new PollyHandler();


                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        UserDTO? user;
                        user = await GetAPIToken(scope);

                        Log.Logger.Information($"Sending Stock Emails at: {DateTime.UtcNow}");

                        await SharedFunctions.PostAsync<object>("Email/SendStockEmail", new object() { }, user.Token.ToString());

                        Log.Logger.Information($"Email Stock Sender Worker running at: {DateTime.UtcNow}");
                        await Task.Delay(TimeSpan.FromDays(7), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error($"Email Stock Sender Worker with error: {ex.Message}", ex.Message, ex.InnerException);
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }
            }
        }

        private async Task<UserDTO?> GetAPIToken(IServiceScope scope)
        {
            string key = string.Empty;

            _kmsProvider = scope.ServiceProvider.GetRequiredService<AWSKMSKeyProvider>();
            key = _kmsProvider.GetKMSKey(_configuration.GetSection("SelfAPIAuth").Value ?? string.Empty);

            return await SharedFunctions.GetAsync<UserDTO>("User/LogInto", key);
        }
    }
}
