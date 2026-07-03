using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EvangelionERPV2.Worker.EmailModule.EmailWorker
{
    public sealed class EmailMonthlyOrderSenderWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;

        public EmailMonthlyOrderSenderWorker(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
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

                        Log.Logger.Information($"Sending Monthly Order Emails at: {DateTime.UtcNow}");

                        if (user == null || string.IsNullOrWhiteSpace(user.Token))
                        {
                            Log.Logger.Warning("Email Monthly Order Sender Worker could not obtain an API token.");
                        }
                        else
                        {
                            await SharedFunctions.PostAsync<object>("Email/SendMonthlyBillingBroadcast", new object(), user.Token);
                        }

                        Log.Logger.Information($"Email Monthly Order Sender Worker running at: {DateTime.UtcNow}");
                        await Task.Delay(TimeSpan.FromDays(7), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error("Email Monthly Order Sender Worker with error. ErrorType={ErrorType}", ex.GetType().Name);
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }
            }
        }

        private async Task<UserDTO?> GetAPIToken(IServiceScope scope)
        {
            string key = string.Empty;

            var kmsProvider = scope.ServiceProvider.GetRequiredService<AWSKMSKeyProvider>();
            key = kmsProvider.GetKMSKey(_configuration.GetSection("SelfAPIAuth").Value ?? string.Empty);

            var loginRequest = BuildLoginRequest(key);
            if (loginRequest == null)
            {
                Log.Logger.Warning("Email Monthly Order Sender Worker SelfAPIAuth credentials are missing or invalid.");
                return null;
            }

            return await SharedFunctions.PostAsync<UserDTO>("User/LogInto", loginRequest);
        }

        private static LoginRequestDTO? BuildLoginRequest(string key)
        {
            if (!SelfApiCredential.TryParse(key, out var userName, out var password))
                return null;

            return new LoginRequestDTO
            {
                UserName = userName,
                Password = password
            };
        }
    }
}
