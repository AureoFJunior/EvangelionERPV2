using EvangelionERPV2.Shared.Entities.RabbitMQ;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Options;

namespace EvangelionERPV2.EmailModule.Application.Services
{
    public class EmailRabbitMQManager : RabbitMQManagerBase<EmailChannelSettings>, IEmailRabbitMQManager
    {
        public EmailRabbitMQManager(
            IOptions<RabbitMQSettings> rabbitSettings,
            IOptions<EmailChannelSettings> channelSettings,
            AWSKMSKeyProvider kmsProvider)
            : base(rabbitSettings, channelSettings, kmsProvider)
        {
        }
    }
}