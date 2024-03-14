using EvangelionERPV2.Domain.Interfaces;
using EvangelionERPV2.Domain.Interfaces.Services;
using EvangelionERPV2.Domain.Models;
using EvangelionERPV2.Domain.Models.RabbitMQ;
using EvangelionERPV2.Domain.Utils;
using Microsoft.Extensions.Options;
using MimeKit;
using Serilog;

namespace EvangelionERPV2.Web.Worker.EmailWorker
{
    public sealed class EmailWorker : BackgroundService
    {
        public readonly IEmailService<Email> _emailService;
        public readonly IRabbitMQManager _rabbitMQManager;
        public readonly IOptions<EmailChannelSettings> _baseChannelSettings;

        public EmailWorker(IEmailService<Email> emailService, 
            IRabbitMQManager rabbitMQManager, 
            IOptions<EmailChannelSettings> baseChannelSettings)
        {
            _emailService = emailService;
            _rabbitMQManager = rabbitMQManager;
            _baseChannelSettings = baseChannelSettings;
        }

        public EmailWorker()
        {
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Get from Email Queue and send
                var message = await _rabbitMQManager.DequeueAndProcessAsync<MimeMessage>(_baseChannelSettings.Value);

                await _emailService.SendEmail(message);

                Log.Logger.Information($"Email Worker running at: {DateTime.UtcNow}");
                await Task.Delay(1_000, stoppingToken);
            }
        }
    }
}
