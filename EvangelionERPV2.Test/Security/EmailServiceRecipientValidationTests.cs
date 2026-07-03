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
using System.Net;
using System.Reflection;

namespace EvangelionERPV2.Test.Security
{
    public class EmailServiceRecipientValidationTests
    {
        [Fact]
        public async Task ShouldSendEmail_RemovesInvalidRecipients_AndReturnsTrueWhenAnyValid()
        {
            var service = CreateService();
            var email = new EmailStructure(
                "body",
                "subject",
                new[] { "valid@example.com", "invalid-recipient" });

            var result = await InvokeShouldSendEmailAsync(service, email);

            Assert.True(result);
            Assert.Single(email.RecipientEmails);
            Assert.Equal("valid@example.com", email.RecipientEmails.First());
        }

        [Fact]
        public async Task ShouldSendEmail_ReturnsFalse_WhenNoValidRecipientsRemain()
        {
            var service = CreateService();
            var email = new EmailStructure(
                "body",
                "subject",
                new[] { "invalid-recipient", "still-invalid" });

            var result = await InvokeShouldSendEmailAsync(service, email);

            Assert.False(result);
            Assert.Empty(email.RecipientEmails);
        }

        [Fact]
        public async Task ResolveAllowedSmtpAddressesAsync_WhenHostResolvesToLocalhost_ReturnsEmpty()
        {
            var method = typeof(EmailService).GetMethod(
                "ResolveAllowedSmtpAddressesAsync",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var task = method!.Invoke(null, ["localhost"]) as Task<IPAddress[]>;

            Assert.NotNull(task);
            Assert.Empty(await task!);
        }

        private static EmailService CreateService()
        {
            var rabbitMqManager = new Mock<IEmailRabbitMQManager>(MockBehavior.Strict).Object;
            var enterpriseRepository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict).Object;
            var orderService = new Mock<IOrderService<Order>>(MockBehavior.Strict).Object;
            var productService = new Mock<IProductService<Product>>(MockBehavior.Strict).Object;
            var emailRepository = new Mock<IRepository<Email>>(MockBehavior.Strict).Object;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object, configuration);

            return new EmailService(
                rabbitMqManager,
                enterpriseRepository,
                orderService,
                productService,
                emailRepository,
                kmsProvider,
                configuration);
        }

        private static async Task<bool> InvokeShouldSendEmailAsync(EmailService service, EmailStructure email)
        {
            var method = typeof(EmailService).GetMethod("ShouldSendEmail", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var enterprise = new Enterprise();
            var task = method!.Invoke(service, new object[] { email, enterprise }) as Task<bool>;

            Assert.NotNull(task);
            return await task!;
        }
    }
}
