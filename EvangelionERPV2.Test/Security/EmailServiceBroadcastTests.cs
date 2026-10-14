using Amazon.SecretsManager;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.EmailModule.Application.Services;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Linq.Expressions;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The enterprise repository reports "no rows" by throwing NotFoundDatabaseException. A
    /// scheduled broadcast with nothing to send must stay a no-op instead of surfacing an error.
    /// </summary>
    public class EmailServiceBroadcastTests
    {
        [Fact]
        public async Task SendMonthEmail_WhenNoEnterpriseOptedIn_DoesNotThrow()
        {
            var enterpriseRepository = new Mock<IRepository<Enterprise>>();
            enterpriseRepository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Enterprise, bool>>>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            var service = CreateService(enterpriseRepository.Object);

            await service.SendMonthEmail();
        }

        [Fact]
        public async Task SendStockEmail_WhenNoEnterpriseMatches_DoesNotThrow()
        {
            var enterpriseRepository = new Mock<IRepository<Enterprise>>();
            enterpriseRepository
                .Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<Enterprise, bool>>>()))
                .ThrowsAsync(new NotFoundDatabaseException());

            var service = CreateService(enterpriseRepository.Object);

            await service.SendStockEmail();
        }

        private static EmailService CreateService(IRepository<Enterprise> enterpriseRepository)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

            return new EmailService(
                new Mock<IEmailRabbitMQManager>(MockBehavior.Strict).Object,
                enterpriseRepository,
                new Mock<IOrderService<Order>>(MockBehavior.Strict).Object,
                new Mock<IProductService<Product>>(MockBehavior.Strict).Object,
                new Mock<IRepository<Email>>(MockBehavior.Strict).Object,
                new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object, configuration),
                configuration);
        }
    }
}
