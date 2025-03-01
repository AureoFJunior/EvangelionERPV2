using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Worker.OrderModule.OrderWorker.RabbitMQ;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EvangelionERPV2.Worker.OrderModule.OrderWorker
{
    public sealed class OrderWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private AWSKMSKeyProvider _kmsProvider;
        private readonly IConfiguration _configuration;

        public OrderWorker(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
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
                    try
                    {
                        using (var messageScope = _serviceScopeFactory.CreateScope())
                        {
                            var scopedRabbitMQ = messageScope.ServiceProvider.GetRequiredService<IRabbitMQManager>();
                            _kmsProvider = messageScope.ServiceProvider.GetRequiredService<AWSKMSKeyProvider>();
                            key = _kmsProvider.GetKMSKey(_configuration.GetSection("SelfAPIAuth").Value ?? string.Empty);
                            // Get Order from Queue and save
                            var order = await scopedRabbitMQ.DequeueAndProcessAsync<Order>();
                            if (order != null)
                            {
                                Log.Logger.Information($"Creating Order at: {DateTime.UtcNow}");

                                var user = await SharedFunctions.GetAsync<UserDTO>("User/LogInto", key);
                                await SharedFunctions.PostAsync<object>("Order/InsertOrder", order, user.Token.ToString());
                            }
                            Log.Logger.Information($"Order Worker running at: {DateTime.UtcNow}");
                        }

                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error($"Order Worker with error: {ex.Message}", ex.Message, ex.InnerException);
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Order Worker Scope with error: {ex.Message}", ex.Message, ex.InnerException);
            }
        }
    }
}
