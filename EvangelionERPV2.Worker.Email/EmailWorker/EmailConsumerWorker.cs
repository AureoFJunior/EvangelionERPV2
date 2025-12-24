using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Worker.EmailModule.EmailWorker.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MimeKit;
using Serilog;

namespace EvangelionERPV2.Worker.EmailModule.EmailWorker
{
    public sealed class EmailConsumerWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        private AWSKMSKeyProvider _kmsProvider;

        public EmailConsumerWorker(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                string key = string.Empty;
                while (!stoppingToken.IsCancellationRequested)
                {
                    using (var scope = _serviceScopeFactory.CreateScope())
                    {
                        try
                        {
                            var rabbitMQManager = scope.ServiceProvider.GetRequiredService<IRabbitMQManager>();

                            // Get from Email Queue and send
                            var rawMessage = await rabbitMQManager.DequeueAndProcessAsync<string>();

                            if (!string.IsNullOrEmpty(rawMessage))
                            {
                                UserDTO? user;
                                user = await GetAPIToken(scope);

                                Log.Logger.Information($"Sending Email at: {DateTime.UtcNow}");

                                await SharedFunctions.PostAsync<object>("Email/SendEmail", rawMessage, user.Token.ToString());
                            }

                            Log.Logger.Information($"Email Consumer Worker running at: {DateTime.UtcNow}");
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Error($"Email Consumer Worker error: {ex.Message}", ex.Message, ex.InnerException);
                        }
                        finally
                        {
                            // Delay before the next loop iteration
                            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Email Consumer With Scope error: {ex.Message}", ex.Message, ex.InnerException);
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
