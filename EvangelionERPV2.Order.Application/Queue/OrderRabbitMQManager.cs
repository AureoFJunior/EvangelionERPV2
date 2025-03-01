
using EvangelionERPV2.Shared.Entities.RabbitMQ;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Options;

namespace EvangelionERPV2.OrderModule.Application.Services
{
    public class OrderRabbitMQManager : RabbitMQManagerBase<OrderChannelSettings>, IOrderRabbitMQManager
    {
        public OrderRabbitMQManager(
            IOptions<RabbitMQSettings> rabbitSettings,
            IOptions<OrderChannelSettings> channelSettings,
            AWSKMSKeyProvider kmsProvider)
            : base(rabbitSettings, channelSettings, kmsProvider)
        {
        }
    }
}