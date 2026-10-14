using Amazon.SecretsManager;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.EmailModule.Application.Services;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Text.Json;

namespace EvangelionERPV2.Test.Security
{
    public class EmailServiceQueuedEmailTests
    {
        [Fact]
        public async Task SendManualEmail_QueuesSignedPayloadWithOriginalEnterprise()
        {
            var enterpriseId = Guid.NewGuid();
            string? queuedPayload = null;

            var rabbitMqManager = new Mock<IEmailRabbitMQManager>(MockBehavior.Strict);
            rabbitMqManager
                .Setup(manager => manager.EnqueueAsync(It.IsAny<string>()))
                .Callback<string>(payload => queuedPayload = payload)
                .Returns(Task.CompletedTask);

            var emailRepository = new Mock<IRepository<Email>>(MockBehavior.Strict);
            emailRepository
                .Setup(repository => repository.GetAllAsync(It.IsAny<Func<Email, bool>?>()))
                .ReturnsAsync((Func<Email, bool>? predicate) =>
                {
                    var settings = new List<Email>
                    {
                        new()
                        {
                            Id = Guid.NewGuid(),
                            EnterpriseId = enterpriseId,
                            HostName = "smtp.example.com",
                            UserName = "sender@example.com",
                            Password = "password",
                            Port = 587,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        }
                    };

                    return predicate == null ? settings : settings.Where(predicate).ToList();
                });

            var service = CreateService(rabbitMqManager.Object, emailRepository.Object);

            await service.SendManualEmail(
                new EmailStructure("Body", "Subject", ["recipient@example.com"]),
                new Enterprise { Id = enterpriseId, Email = "recipient@example.com" });

            Assert.False(string.IsNullOrWhiteSpace(queuedPayload));
            using var document = JsonDocument.Parse(queuedPayload!);
            Assert.Equal(enterpriseId, document.RootElement.GetProperty("EnterpriseId").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("RawMimeMessage").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("Signature").GetString()));
        }

        private static EmailService CreateService(
            IEmailRabbitMQManager rabbitMqManager,
            IRepository<Email> emailRepository)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EmailQueue:SigningKey"] = "queue-signing-key"
                })
                .Build();
            var kmsProvider = new AWSKMSKeyProvider(
                new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object,
                configuration);

            return new EmailService(
                rabbitMqManager,
                new Mock<IRepository<Enterprise>>(MockBehavior.Strict).Object,
                new Mock<IOrderService<Order>>(MockBehavior.Strict).Object,
                new Mock<IProductService<Product>>(MockBehavior.Strict).Object,
                emailRepository,
                kmsProvider,
                configuration);
        }
    }
}
