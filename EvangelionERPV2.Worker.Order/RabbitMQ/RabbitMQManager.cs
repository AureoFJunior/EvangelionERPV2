using EvangelionERPV2.Shared.Entities.RabbitMQ;
using Microsoft.Extensions.Options;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Worker.OrderModule.OrderWorker.RabbitMQ
{
    public class RabbitMQManager : RabbitMQManagerBase<OrderChannelSettings>, IRabbitMQManager
    {
        public RabbitMQManager(
            IOptions<RabbitMQSettings> rabbitSettings,
            IOptions<OrderChannelSettings> channelSettings,
            AWSKMSKeyProvider kmsProvider)
            : base(rabbitSettings, channelSettings, kmsProvider)
        {
        }
    }
}
