using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Enrolling a till mints the credential that lets it reach a login endpoint with no reCAPTCHA,
    /// so it sits at supervisor tier and is scoped to the caller's own enterprise.
    /// </summary>
    public class PdvControllerEnrollTerminalTests
    {
        [Fact]
        public void EnrollTerminal_RequiresSupervisorTierPermission()
        {
            ControllerPolicyTestHelper.AssertActionPolicy<PdvController>(
                nameof(PdvController.EnrollTerminal),
                "rbac:" + RbacPermissions.Pdv.CashMovement);
        }

        [Fact]
        public async Task EnrollTerminal_ReturnsUnauthorized_WhenEnterpriseClaimIsMissing()
        {
            var repository = new Mock<IRepository<PdvTerminal>>(MockBehavior.Strict);
            var controller = CreateController(repository, claims: []);

            var response = await controller.EnrollTerminal(new EnrollTerminalRequestDTO { TerminalId = "CAIXA-01" });

            Assert.IsType<UnauthorizedResult>(response);
        }

        [Fact]
        public async Task EnrollTerminal_ReturnsTheCredentialOnce_AndStoresOnlyAVerifier()
        {
            var enterpriseId = Guid.NewGuid();
            PdvTerminal? stored = null;

            var repository = new Mock<IRepository<PdvTerminal>>(MockBehavior.Strict);
            repository.Setup(x => x.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PdvTerminal, bool>>>()))
                .ReturnsAsync([]);
            repository.Setup(x => x.CreateAsync(It.IsAny<PdvTerminal>()))
                .Callback<PdvTerminal>(terminal => stored = terminal)
                .ReturnsAsync((PdvTerminal terminal) => terminal);
            repository.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var controller = CreateController(repository, ClaimsFor(enterpriseId));

            var response = await controller.EnrollTerminal(new EnrollTerminalRequestDTO { TerminalId = "CAIXA-01" });

            var ok = Assert.IsType<OkObjectResult>(response);
            var dto = Assert.IsType<EnrollTerminalResponseDTO>(ok.Value);

            Assert.NotNull(stored);
            Assert.Equal(enterpriseId, stored!.EnterpriseId);

            Assert.NotEqual(dto.Credential, stored.CredentialVerifier);
            Assert.DoesNotContain(dto.Credential, stored.CredentialVerifier);
            Assert.True(stored.Verify(dto.Credential));
        }

        [Fact]
        public async Task EnrollTerminal_Refuses_WhenTheTerminalIsAlreadyEnrolled()
        {
            var enterpriseId = Guid.NewGuid();
            var repository = new Mock<IRepository<PdvTerminal>>(MockBehavior.Strict);
            repository.Setup(x => x.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<PdvTerminal, bool>>>()))
                .ReturnsAsync([PdvTerminal.Enroll(enterpriseId, "CAIXA-01", "already")]);

            var controller = CreateController(repository, ClaimsFor(enterpriseId));

            var response = await controller.EnrollTerminal(new EnrollTerminalRequestDTO { TerminalId = "CAIXA-01" });

            Assert.IsType<ConflictObjectResult>(response);
            repository.Verify(x => x.CreateAsync(It.IsAny<PdvTerminal>()), Times.Never);
        }

        [Fact]
        public async Task EnrollTerminal_RejectsAnEmptyTerminalId()
        {
            var repository = new Mock<IRepository<PdvTerminal>>(MockBehavior.Strict);
            var controller = CreateController(repository, ClaimsFor(Guid.NewGuid()));

            var response = await controller.EnrollTerminal(new EnrollTerminalRequestDTO { TerminalId = "   " });

            Assert.IsType<BadRequestObjectResult>(response);
        }

        private static Claim[] ClaimsFor(Guid enterpriseId) =>
            [new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())];

        private static PdvController CreateController(Mock<IRepository<PdvTerminal>> repository, Claim[] claims)
        {
            return new PdvController(
                new Mock<IPdvService>(MockBehavior.Strict).Object,
                repository.Object,
                new Mock<IRepository<User>>(MockBehavior.Strict).Object,
                new Mock<IRepository<PdvOverrideAudit>>().Object,
                new Mock<IRepository<Enterprise>>().Object,
                new Mock<INFeService<NFeDocument>>().Object,
                FiscalSettings(),
                new PasswordLoginService(new Mock<IRepository<User>>(MockBehavior.Strict).Object),
                new TokenService(new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object,
                    new ConfigurationBuilder().AddInMemoryCollection().Build()))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
                    }
                }
            };
        }

        /// <summary>A tenant with NFC-e switched on. The gate is exercised in its own test.</summary>
        private static INFeFiscalSettingsService FiscalSettings(bool enabled = true, bool nfce = true)
        {
            var settings = new Mock<INFeFiscalSettingsService>();
            settings.Setup(x => x.GetSettingsAsync(It.IsAny<Guid>()))
                .ReturnsAsync(new EnterpriseFiscalSettings { Enabled = enabled, NFCeEnabled = nfce });

            return settings.Object;
        }
    }
}
