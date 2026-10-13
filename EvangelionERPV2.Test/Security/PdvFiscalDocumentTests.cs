using System.Security.Claims;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The till asking the ERP for its NFC-e.
    /// </summary>
    public class PdvFiscalDocumentTests
    {
        private static readonly Guid EnterpriseId = Guid.NewGuid();
        private static readonly Guid ClientSaleId = Guid.NewGuid();
        private static readonly Guid OrderId = Guid.NewGuid();

        [Fact]
        public async Task AnAuthorisedNote_ComesBackWithWhatTheTillMustPrint()
        {
            var nfe = new Mock<INFeService<NFeDocument>>();
            nfe.Setup(x => x.IssueAsync(OrderId, EnterpriseId, NFeDocumentType.NFCe, null))
                .ReturnsAsync(Document(NFeStatus.Authorized, qr: "the-qr"));

            var result = await Controller(SyncedSale(), nfe).IssueFiscalDocument(ClientSaleId);

            var payload = Assert.IsType<PdvFiscalDocumentDTO>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.Equal("the-qr", payload.QrCode);
            Assert.Equal((int)NFeStatus.Authorized, payload.Status);
            Assert.False(payload.IsContingency);
        }

        [Fact]
        public async Task AProvablyUntransmittedFailure_FallsBackToContingency()
        {
            var nfe = new Mock<INFeService<NFeDocument>>();

            nfe.Setup(x => x.IssueAsync(OrderId, EnterpriseId, NFeDocumentType.NFCe, null))
                .ReturnsAsync(Document(NFeStatus.Error, transmittedAt: null));
            nfe.Setup(x => x.IssueAsync(OrderId, EnterpriseId, NFeDocumentType.NFCe, It.IsAny<NFeContingency>()))
                .ReturnsAsync(Document(NFeStatus.Authorized, qr: "offline-qr", tipoEmissao: 9));

            var result = await Controller(SyncedSale(), nfe)
                .IssueFiscalDocument(ClientSaleId, "SEFAZ inacessivel no momento da venda");

            var payload = Assert.IsType<PdvFiscalDocumentDTO>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.True(payload.IsContingency);
            Assert.Equal("offline-qr", payload.QrCode);
        }

        [Fact]
        public async Task AnUnknownOutcome_IsNeverReissuedOffline()
        {
            var nfe = new Mock<INFeService<NFeDocument>>(MockBehavior.Strict);
            nfe.Setup(x => x.IssueAsync(OrderId, EnterpriseId, NFeDocumentType.NFCe, null))
                .ReturnsAsync(Document(NFeStatus.Pending, transmittedAt: DateTime.UtcNow));

            var result = await Controller(SyncedSale(), nfe)
                .IssueFiscalDocument(ClientSaleId, "SEFAZ inacessivel no momento da venda");

            var payload = Assert.IsType<PdvFiscalDocumentDTO>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.False(payload.IsContingency);

            nfe.Verify(x => x.IssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<NFeDocumentType>(),
                It.Is<NFeContingency>(contingency => contingency != null)), Times.Never);
        }

        [Fact]
        public async Task WithoutAReason_NothingFallsBackToContingency()
        {
            var nfe = new Mock<INFeService<NFeDocument>>(MockBehavior.Strict);
            nfe.Setup(x => x.IssueAsync(OrderId, EnterpriseId, NFeDocumentType.NFCe, null))
                .ReturnsAsync(Document(NFeStatus.Error, transmittedAt: null));

            await Controller(SyncedSale(), nfe).IssueFiscalDocument(ClientSaleId);

            nfe.Verify(x => x.IssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<NFeDocumentType>(),
                It.Is<NFeContingency>(contingency => contingency != null)), Times.Never);
        }

        [Theory]
        [InlineData("sem sinal")]
        [InlineData("   caiu a net   ")]
        public async Task AReasonTheSchemaWouldReject_IsRefusedHere(string reason)
        {
            var nfe = new Mock<INFeService<NFeDocument>>(MockBehavior.Strict);
            nfe.Setup(x => x.IssueAsync(OrderId, EnterpriseId, NFeDocumentType.NFCe, null))
                .ReturnsAsync(Document(NFeStatus.Error, transmittedAt: null));

            var result = await Controller(SyncedSale(), nfe).IssueFiscalDocument(ClientSaleId, reason);

            Assert.IsType<BadRequestObjectResult>(result);
            nfe.Verify(x => x.IssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<NFeDocumentType>(),
                It.Is<NFeContingency>(contingency => contingency != null)), Times.Never);
        }

        [Fact]
        public async Task ASaleThatNeverBecameAnOrder_HasNothingToIssueAgainst()
        {
            var nfe = new Mock<INFeService<NFeDocument>>(MockBehavior.Strict);
            var pending = new PdvSale
            {
                EnterpriseId = EnterpriseId,
                ClientSaleId = ClientSaleId,
                Status = (int)EnumPdvSaleStatus.Pending,
                OrderId = null
            };

            var result = await Controller(pending, nfe).IssueFiscalDocument(ClientSaleId);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AShopThatHasNotTurnedNFCeOn_IsRefusedEvenIfTheTillAsks()
        {
            var nfe = new Mock<INFeService<NFeDocument>>(MockBehavior.Strict);

            var result = await Controller(SyncedSale(), nfe, enabled: true, nfceEnabled: false)
                .IssueFiscalDocument(ClientSaleId);

            Assert.IsType<BadRequestObjectResult>(result);

            nfe.Verify(x => x.IssueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<NFeDocumentType>(),
                It.IsAny<NFeContingency>()), Times.Never);
        }

        [Fact]
        public async Task AShopWithFiscalIssuanceOffAltogether_IsRefused()
        {
            var nfe = new Mock<INFeService<NFeDocument>>(MockBehavior.Strict);

            var result = await Controller(SyncedSale(), nfe, enabled: false, nfceEnabled: true)
                .IssueFiscalDocument(ClientSaleId);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task AnotherTenantsSale_IsIndistinguishableFromOneThatNeverExisted()
        {
            var nfe = new Mock<INFeService<NFeDocument>>(MockBehavior.Strict);

            var result = await Controller(null, nfe).IssueFiscalDocument(ClientSaleId);

            Assert.IsType<NotFoundResult>(result);
        }

        private static PdvSale SyncedSale() => new()
        {
            EnterpriseId = EnterpriseId,
            ClientSaleId = ClientSaleId,
            Status = (int)EnumPdvSaleStatus.Synced,
            OrderId = OrderId
        };

        private static NFeDocument Document(
            NFeStatus status, string qr = "", byte tipoEmissao = 1, DateTime? transmittedAt = null) => new()
            {
                AccessKey = "43250811222333000181650010000000071123456780",
                Number = "7",
                Series = tipoEmissao == 9 ? "9" : "1",
                Status = status,
                QrCode = qr,
                UrlChave = qr.Length == 0 ? "" : "https://www.sefaz.rs.gov.br/nfce/consulta",
                TipoEmissao = tipoEmissao,
                TransmittedAtUtc = transmittedAt
            };

        private static PdvController Controller(
            PdvSale? sale, Mock<INFeService<NFeDocument>> nfe, bool enabled = true, bool nfceEnabled = true)
        {
            var pdv = new Mock<IPdvService>();
            pdv.Setup(x => x.GetSaleAsync(ClientSaleId, EnterpriseId)).ReturnsAsync(sale);

            var userRepository = new Mock<IRepository<User>>();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

            var controller = new PdvController(
                pdv.Object,
                new Mock<IRepository<PdvTerminal>>().Object,
                userRepository.Object,
                new Mock<IRepository<PdvOverrideAudit>>().Object,
                new Mock<IRepository<Enterprise>>().Object,
                nfe.Object,
                FiscalSettings(enabled, nfceEnabled),
                new PasswordLoginService(userRepository.Object),
                new TokenService(new Mock<IRepository<RefreshToken>>().Object, configuration));

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Sid, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.GroupSid, EnterpriseId.ToString())
            ], "test");

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            };

            return controller;
        }

        /// <summary>
        /// The tenant's own switches, which the endpoint checks rather than trusting the terminal.
        /// </summary>
        private static INFeFiscalSettingsService FiscalSettings(bool enabled, bool nfceEnabled)
        {
            var settings = new Mock<INFeFiscalSettingsService>();
            settings.Setup(x => x.GetSettingsAsync(It.IsAny<Guid>()))
                .ReturnsAsync(new EnterpriseFiscalSettings { Enabled = enabled, NFCeEnabled = nfceEnabled });

            return settings.Object;
        }
    }
}
