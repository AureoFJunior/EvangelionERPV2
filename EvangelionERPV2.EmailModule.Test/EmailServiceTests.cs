using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Amazon.SecretsManager;

using EvangelionERPV2.EmailModule.Application.Services;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;

using Microsoft.Extensions.Configuration;

using Moq;

namespace EvangelionERPV2.EmailModule.Test
{
    /// <summary>
    /// Everything here stops short of a socket. Two things are worth pinning without one: that a
    /// queued payload nobody signed is refused before it can be sent, and that the message the
    /// builder produces is one a mail client will actually render.
    /// </summary>
    public class EmailServiceTests
    {
        private const string SigningKey = "test-signing-key";

        private static readonly Guid EnterpriseId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        #region Queued payload

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not json at all")]
        [InlineData("{}")]
        public async Task TrySendQueuedEmail_WithAnUnreadablePayload_RefusesIt(string payload)
        {
            var context = new ServiceContext();

            Assert.False(await context.Service.TrySendQueuedEmail(payload));
        }

        [Fact]
        public async Task TrySendQueuedEmail_WithNoSignature_RefusesIt()
        {
            var context = new ServiceContext();

            var payload = JsonSerializer.Serialize(new
            {
                Version = 1,
                EnterpriseId,
                RawMimeMessage = "Subject: hi\r\n\r\nbody",
                Signature = string.Empty
            });

            Assert.False(await context.Service.TrySendQueuedEmail(payload));
        }

        [Fact]
        public async Task TrySendQueuedEmail_WithASignatureForDifferentContent_RefusesIt()
        {
            var context = new ServiceContext();

            var payload = JsonSerializer.Serialize(new
            {
                Version = 1,
                EnterpriseId,
                RawMimeMessage = "Subject: tampered\r\n\r\nbody",
                Signature = Sign(EnterpriseId, "Subject: original\r\n\r\nbody")
            });

            Assert.False(await context.Service.TrySendQueuedEmail(payload));
        }

        [Fact]
        public async Task TrySendQueuedEmail_SignedForAnotherTenant_RefusesIt()
        {
            var context = new ServiceContext();
            var rawMessage = "Subject: hi\r\n\r\nbody";

            var payload = JsonSerializer.Serialize(new
            {
                Version = 1,
                EnterpriseId,
                RawMimeMessage = rawMessage,
                Signature = Sign(Guid.NewGuid(), rawMessage)
            });

            Assert.False(await context.Service.TrySendQueuedEmail(payload));
        }

        [Fact]
        public async Task TrySendQueuedEmail_WithASignatureThatIsNotBase64_RefusesIt()
        {
            var context = new ServiceContext();

            var payload = JsonSerializer.Serialize(new
            {
                Version = 1,
                EnterpriseId,
                RawMimeMessage = "Subject: hi\r\n\r\nbody",
                Signature = "!!!not base64!!!"
            });

            Assert.False(await context.Service.TrySendQueuedEmail(payload));
        }

        #endregion

        #region Building the message

        [Fact]
        public async Task CreateEmail_MovesDataUriImagesIntoLinkedResources()
        {
            var context = new ServiceContext();

            const string gif = "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

            var message = await context.Service.CreateEmail(new EmailStructure
            {
                Subject = "Boleto",
                Body = $"<p>Segue</p><img src=\"data:image/gif;base64,{gif}\" />",
                RecipientEmails = ["cliente@example.com"]
            });

            var html = message.HtmlBody;

            Assert.DoesNotContain("data:image", html);
            Assert.Contains("cid:", html);
        }

        [Fact]
        public async Task CreateEmail_SkipsAnAttachmentWithNoContent()
        {
            var context = new ServiceContext();

            var message = await context.Service.CreateEmail(new EmailStructure
            {
                Subject = "Boleto",
                Body = "<p>Segue</p>",
                RecipientEmails = ["cliente@example.com"],
                Attachments =
                [
                    new EmailAttachment { FileName = "empty.pdf", ContentType = "application/pdf", Content = [] },
                    new EmailAttachment { FileName = "real.pdf", ContentType = "application/pdf", Content = [1, 2, 3] }
                ]
            });

            var attachment = Assert.Single(message.Attachments);
            Assert.Equal("real.pdf", attachment.ContentDisposition?.FileName);
        }

        [Fact]
        public async Task CreateEmail_AddsEveryRecipientAndSendsFromTheStoredAccount()
        {
            var context = new ServiceContext();

            var message = await context.Service.CreateEmail(new EmailStructure
            {
                Subject = "Relatório",
                Body = "<p>ok</p>",
                RecipientEmails = ["a@example.com", "b@example.com"]
            });

            Assert.Equal(2, message.To.Count);
            Assert.Contains("erp@example.com", message.From.ToString());
        }

        #endregion

        private static string Sign(Guid enterpriseId, string rawMimeMessage)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SigningKey));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{enterpriseId:N}\n{rawMimeMessage}")));
        }

        private sealed class ServiceContext
        {
            public EmailService Service { get; }

            public ServiceContext()
            {
                var storedSettings = new List<Email>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        HostName = "smtp.example.com",
                        UserName = "erp@example.com",
                        Password = "unused-in-these-tests",
                        Port = 587,
                        EnterpriseId = EnterpriseId,
                        IsActive = true
                    }
                };

                var emailRepository = new Mock<IRepository<Email>>();
                emailRepository
                    .Setup(x => x.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Email, bool>>?>()))
                    .ReturnsAsync((System.Linq.Expressions.Expression<Func<Email, bool>>? predicate) =>
                        predicate == null ? storedSettings : storedSettings.Where(predicate.Compile()).ToList());

                var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["EmailQueue:SigningKey"] = SigningKey
                    })
                    .Build();

                var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>().Object, configuration);

                Service = new EmailService(
                    new Mock<IEmailRabbitMQManager>().Object,
                    new Mock<IRepository<Enterprise>>().Object,
                    new Mock<IOrderService<Order>>().Object,
                    new Mock<IProductService<Product>>().Object,
                    emailRepository.Object,
                    kmsProvider,
                    configuration);
            }
        }
    }
}
