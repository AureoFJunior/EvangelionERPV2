using EvangelionERPV2.Shared.Entities.RabbitMQ;
using Microsoft.Extensions.Options;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Worker.EmailModule.EmailWorker.RabbitMQ
{
    public class RabbitMQManager : RabbitMQManagerBase<EmailChannelSettings>, IRabbitMQManager
    {
        public RabbitMQManager(
            IOptions<RabbitMQSettings> rabbitSettings,
            IOptions<EmailChannelSettings> channelSettings,
            AWSKMSKeyProvider kmsProvider)
            : base(rabbitSettings, channelSettings, kmsProvider)
        {
        }
    }
}
