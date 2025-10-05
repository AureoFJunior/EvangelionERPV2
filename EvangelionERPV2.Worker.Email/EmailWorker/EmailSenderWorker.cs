using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EvangelionERPV2.Worker.EmailModule.EmailWorker
{
    public sealed class EmailSenderWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public EmailSenderWorker(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
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
                        Log.Logger.Information($"Sending Emails at: {DateTime.UtcNow}");
                        var user = await SharedFunctions.GetAsync<UserDTO>("User/LogInto", "admin/1234");
                        await SharedFunctions.PostAsync<object>("Email/SendMonthEmail", new object() { }, user.Token.ToString());

                        Log.Logger.Information($"Email Sender Worker running at: {DateTime.UtcNow}");
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error($"Email Sender Worker with error: {ex.Message}", ex.Message, ex.InnerException);
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }
            }
        }
    }
}
