using System.Text;
using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.EmailModule.Application.Services;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Infra.Certificates;
using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    public class DashboardControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Mock<IDailyPanelService> _service = new();

        [Fact]
        public async Task Today_WithoutATenantClaim_IsUnauthorized()
        {
            var controller = new DashboardController(_service.Object).WithoutTenant();

            Assert.IsType<UnauthorizedResult>(await controller.Today());
        }

        [Fact]
        public async Task Today_AnswersThePanelForTheCallersTenantOnly()
        {
            _service.Setup(s => s.GetTodayAsync(_enterpriseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DailyPanelDTO());

            var controller = new DashboardController(_service.Object).WithTenant(_enterpriseId);

            Assert.IsType<OkObjectResult>(await controller.Today());
            _service.VerifyAll();
        }
    }

    /// <summary>
    /// A redrive replays real work — orders that will be created, e-mails that will be sent — and
    /// the work queues are not tenant-scoped, so one request touches every enterprise on the broker.
    /// The bounds are the whole endpoint.
    /// </summary>
    public class MessagingControllerTests
    {
        private readonly Mock<IOrderRabbitMQManager> _orderManager = new();
        private readonly Mock<IEmailRabbitMQManager> _emailManager = new();

        private MessagingController Controller() =>
            new MessagingController(_orderManager.Object, _emailManager.Object).WithTenant(Guid.NewGuid());

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task RedriveDeadLettered_WithANonPositiveBatch_IsRefused(int maxMessages)
        {
            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().RedriveDeadLettered("order", maxMessages));

            Assert.Equal("maxMessages must be greater than zero.", result.Value);
        }

        [Fact]
        public async Task RedriveDeadLettered_AboveTheBatchCap_IsRefused()
        {
            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().RedriveDeadLettered("order", 501));

            Assert.Equal("maxMessages is capped at 500.", result.Value);
            _orderManager.Verify(
                m => m.RedriveDeadLetteredAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task RedriveDeadLettered_AtExactlyTheCap_IsAllowed()
        {
            _orderManager.Setup(m => m.RedriveDeadLetteredAsync(500, It.IsAny<CancellationToken>())).ReturnsAsync(0);

            Assert.IsType<OkObjectResult>(await Controller().RedriveDeadLettered("order", 500));
            _orderManager.VerifyAll();
        }

        [Theory]
        [InlineData("order")]
        [InlineData("ORDER")]
        [InlineData("  order  ")]
        public async Task RedriveDeadLettered_NamesTheOrderQueueInAnyCasing(string queue)
        {
            _orderManager.Setup(m => m.RedriveDeadLetteredAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(3);

            var result = Assert.IsType<OkObjectResult>(await Controller().RedriveDeadLettered(queue));

            Assert.Contains("redriven", result.Value?.ToString());
            _orderManager.VerifyAll();
        }

        [Fact]
        public async Task RedriveDeadLettered_NamesTheEmailQueue()
        {
            _emailManager.Setup(m => m.RedriveDeadLetteredAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(1);

            Assert.IsType<OkObjectResult>(await Controller().RedriveDeadLettered("email"));
            _emailManager.VerifyAll();
            _orderManager.Verify(
                m => m.RedriveDeadLetteredAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// An unknown queue name is refused rather than silently redriving nothing — an operator who
        /// mistypes must not read a 200 as "there was nothing stuck".
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("orders")]
        [InlineData("nfe")]
        public async Task RedriveDeadLettered_WithAQueueThatDoesNotExist_IsRefused(string? queue)
        {
            var result = Assert.IsType<BadRequestObjectResult>(await Controller().RedriveDeadLettered(queue!));

            Assert.Equal("queue must be either 'order' or 'email'.", result.Value);
        }
    }

    public class NFeFiscalSettingsControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Mock<INFeFiscalSettingsService> _settings = new();
        private readonly Mock<INFeCertificateService> _certificates = new();
        private readonly Mock<IEnterpriseCscProvider> _csc = new();

        private NFeFiscalSettingsController Controller() =>
            new NFeFiscalSettingsController(_settings.Object, _certificates.Object, _csc.Object, Mapper())
                .WithTenant(_enterpriseId);

        private NFeFiscalSettingsController Anonymous() =>
            new NFeFiscalSettingsController(_settings.Object, _certificates.Object, _csc.Object, Mapper())
                .WithoutTenant();

        private static IMapper Mapper()
        {
            var mapper = new Mock<IMapper>();
            mapper.Setup(m => m.Map<EnterpriseFiscalSettingsDTO>(It.IsAny<EnterpriseFiscalSettings>()))
                .Returns<EnterpriseFiscalSettings>(s => new EnterpriseFiscalSettingsDTO { Cnpj = s.Cnpj, Uf = s.Uf });
            mapper.Setup(m => m.Map<EnterpriseFiscalSettings>(It.IsAny<EnterpriseFiscalSettingsDTO>()))
                .Returns<EnterpriseFiscalSettingsDTO>(d => new EnterpriseFiscalSettings { Cnpj = d.Cnpj, Uf = d.Uf });
            return mapper.Object;
        }

        private static UploadNFeCertificateRequestDTO Upload(long length, string password = "senha")
        {
            var file = new Mock<IFormFile>();
            file.SetupGet(f => f.Length).Returns(length);
            file.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns<Stream, CancellationToken>((target, token) =>
                    target.WriteAsync(Encoding.UTF8.GetBytes("pfx"), token).AsTask());

            return new UploadNFeCertificateRequestDTO { File = file.Object, Password = password };
        }

        [Fact]
        public async Task GetFiscalSettings_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetFiscalSettings());
        }

        [Fact]
        public async Task GetFiscalSettings_AnswersTheTenantsRow()
        {
            _settings.Setup(s => s.GetSettingsAsync(_enterpriseId))
                .ReturnsAsync(new EnterpriseFiscalSettings { Cnpj = "11222333000181", Uf = "RS" });

            var result = Assert.IsType<OkObjectResult>(await Controller().GetFiscalSettings());

            Assert.Equal("RS", Assert.IsType<EnterpriseFiscalSettingsDTO>(result.Value).Uf);
        }

        [Fact]
        public async Task UpdateFiscalSettings_SavesAgainstTheTenantOnTheToken()
        {
            Guid? savedFor = null;
            _settings.Setup(s => s.UpsertSettingsAsync(It.IsAny<Guid>(), It.IsAny<EnterpriseFiscalSettings>()))
                .Callback<Guid, EnterpriseFiscalSettings>((id, _) => savedFor = id)
                .ReturnsAsync(new EnterpriseFiscalSettings { Cnpj = "11222333000181", Uf = "RS" });

            Assert.IsType<OkObjectResult>(await Controller().UpdateFiscalSettings(
                new EnterpriseFiscalSettingsDTO { Cnpj = "11222333000181", Uf = "RS" }));

            Assert.Equal(_enterpriseId, savedFor);
        }

        [Fact]
        public async Task UpdateFiscalSettings_WhenTheServiceRefuses_DoesNotEchoTheInnerDetail()
        {
            _settings.Setup(s => s.UpsertSettingsAsync(_enterpriseId, It.IsAny<EnterpriseFiscalSettings>()))
                .ThrowsAsync(new InsertDatabaseException(
                    "outer", new Exception("Violation of UNIQUE KEY 'IX_Fiscal_Secret'")));

            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().UpdateFiscalSettings(new EnterpriseFiscalSettingsDTO()));

            Assert.DoesNotContain("IX_Fiscal_Secret", result.Value?.ToString());
        }

        [Fact]
        public async Task UpdateFiscalSettings_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().UpdateFiscalSettings(new EnterpriseFiscalSettingsDTO()));
        }

        /// <summary>
        /// The CSC itself never reaches the settings row — it goes to the secret store, and only the
        /// id is written next to the tenant. Storing them together would put a shared secret in a
        /// table every fiscal read touches.
        /// </summary>
        [Fact]
        public async Task UpdateCsc_StoresTheSecretSeparatelyAndKeepsOnlyTheId()
        {
            var stored = new EnterpriseFiscalSettings { Cnpj = "11222333000181", Uf = "RS" };
            _csc.Setup(c => c.StoreAsync(_enterpriseId, "ABC123", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _settings.Setup(s => s.GetSettingsAsync(_enterpriseId)).ReturnsAsync(stored);
            _settings.Setup(s => s.UpsertSettingsAsync(_enterpriseId, stored)).ReturnsAsync(stored);

            var result = await Controller().UpdateCsc(new UpdateNFCeCscRequestDTO { CscId = 1, Csc = "ABC123" });

            Assert.IsType<NoContentResult>(result);
            Assert.Equal(1, stored.CscId);
            _csc.VerifyAll();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public async Task UpdateCsc_WithNoCsc_IsRefusedBeforeAnythingIsStored(string? csc)
        {
            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().UpdateCsc(new UpdateNFCeCscRequestDTO { CscId = 1, Csc = csc! }));

            Assert.Equal("A CSC is required.", result.Value);
            _csc.Verify(c => c.StoreAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task UpdateCsc_WithNoCscId_IsRefused(int cscId)
        {
            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().UpdateCsc(new UpdateNFCeCscRequestDTO { CscId = cscId, Csc = "ABC123" }));

            Assert.Equal("A CSC id is required.", result.Value);
        }

        [Fact]
        public async Task UpdateCsc_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().UpdateCsc(new UpdateNFCeCscRequestDTO { CscId = 1, Csc = "ABC123" }));
        }

        [Fact]
        public async Task UploadCertificate_WithNoFile_IsRefused()
        {
            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().UploadCertificate(new UploadNFeCertificateRequestDTO()));

            Assert.Equal("Certificate file is required.", result.Value);
        }

        [Fact]
        public async Task UploadCertificate_OverTheSizeCap_IsRefusedWithoutBuffering()
        {
            var request = Upload((512 * 1024) + 1);

            var result = Assert.IsType<BadRequestObjectResult>(await Controller().UploadCertificate(request));

            Assert.Equal("Certificate file is too large.", result.Value);
            Mock.Get(request.File!).Verify(
                f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UploadCertificate_HandsTheServiceTheBytesAndThePassword()
        {
            string? password = null;
            _certificates.Setup(c => c.UploadAsync(_enterpriseId, It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((Guid _, byte[] _, string pwd, CancellationToken _) => password = pwd)
                .ReturnsAsync(new NFeCertificateStatusDTO());

            Assert.IsType<OkObjectResult>(await Controller().UploadCertificate(Upload(3, "senha-do-pfx")));
            Assert.Equal("senha-do-pfx", password);
        }

        /// <summary>
        /// A wrong password or a corrupt PFX is the operator's problem to see, so this one message
        /// is passed through — it is written by the certificate code, not by the platform.
        /// </summary>
        [Fact]
        public async Task UploadCertificate_WithAWrongPassword_SaysSo()
        {
            _certificates.Setup(c => c.UploadAsync(_enterpriseId, It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NFeCertificateException("The certificate password is incorrect."));

            var result = Assert.IsType<BadRequestObjectResult>(await Controller().UploadCertificate(Upload(3)));

            Assert.Equal("The certificate password is incorrect.", result.Value);
        }

        [Fact]
        public async Task UploadCertificate_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().UploadCertificate(Upload(3)));
        }

        [Fact]
        public async Task GetCertificateStatus_AnswersTheTenantsStatus()
        {
            _certificates.Setup(c => c.GetStatusAsync(_enterpriseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NFeCertificateStatusDTO());

            Assert.IsType<OkObjectResult>(await Controller().GetCertificateStatus());
            _certificates.VerifyAll();
        }

        [Fact]
        public async Task GetCertificateStatus_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetCertificateStatus());
        }

        [Fact]
        public async Task DeleteCertificate_RemovesItForTheCallersTenantOnly()
        {
            _certificates.Setup(c => c.DeleteAsync(_enterpriseId, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            Assert.IsType<NoContentResult>(await Controller().DeleteCertificate());
            _certificates.VerifyAll();
        }

        [Fact]
        public async Task DeleteCertificate_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().DeleteCertificate());
        }
    }
}
