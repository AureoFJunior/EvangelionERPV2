using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Linq.Expressions;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The intake for overrides a till reports. Refused ones matter most: they are what a lockout
    /// counts, and they are the only trace a PIN-guessing attempt leaves.
    /// </summary>
    public class PdvControllerOverrideTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _operatorUserId = Guid.NewGuid();
        private readonly List<PdvOverrideAudit> _stored = [];

        [Fact]
        public void RegisterOverride_RequiresATillGrant()
        {
            ControllerPolicyTestHelper.AssertActionPolicy<PdvController>(
                nameof(PdvController.RegisterOverride),
                "rbac:" + RbacPermissions.Pdv.Operate);
        }

        [Fact]
        public async Task RegisterOverride_RecordsARefusedAttempt()
        {
            var controller = CreateController();

            var response = await controller.RegisterOverride(Request(succeeded: false));

            var dto = Assert.IsType<RegisterOverrideResponseDTO>(Assert.IsType<OkObjectResult>(response).Value);
            Assert.False(dto.WasAlreadyRecorded);

            var audit = Assert.Single(_stored);
            Assert.False(audit.Succeeded);
            Assert.Equal(_enterpriseId, audit.EnterpriseId);

            Assert.Equal(_operatorUserId, audit.OperatorUserId);
        }

        [Fact]
        public async Task RegisterOverride_IsIdempotentOnClientOverrideId()
        {
            var controller = CreateController();
            var request = Request(succeeded: false);

            await controller.RegisterOverride(request);
            var response = await controller.RegisterOverride(request);

            var dto = Assert.IsType<RegisterOverrideResponseDTO>(Assert.IsType<OkObjectResult>(response).Value);

            Assert.True(dto.WasAlreadyRecorded);
            Assert.Single(_stored);
        }

        [Fact]
        public async Task RegisterOverride_RejectsAnEmptyOverrideId()
        {
            var controller = CreateController();

            var request = Request(succeeded: true);
            request.ClientOverrideId = Guid.Empty;

            Assert.IsType<BadRequestObjectResult>(await controller.RegisterOverride(request));
            Assert.Empty(_stored);
        }

        [Fact]
        public async Task RegisterOverride_ReturnsUnauthorized_WhenEnterpriseClaimIsMissing()
        {
            var controller = CreateController(claims: []);

            Assert.IsType<UnauthorizedResult>(await controller.RegisterOverride(Request(succeeded: true)));
            Assert.Empty(_stored);
        }

        private RegisterOverrideRequestDTO Request(bool succeeded) => new()
        {
            ClientOverrideId = Guid.NewGuid(),
            SupervisorUserId = Guid.NewGuid(),
            TerminalId = "CAIXA-01",
            ClientSaleId = Guid.NewGuid(),
            Permission = RbacPermissions.Pdv.VoidItem,
            Succeeded = succeeded,
            OccurredAt = DateTime.UtcNow
        };

        private PdvController CreateController(Claim[]? claims = null)
        {
            var overrideAuditRepository = new Mock<IRepository<PdvOverrideAudit>>();
            overrideAuditRepository
                .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<PdvOverrideAudit, bool>>>()))
                .ReturnsAsync((int? _, int? __, Expression<Func<PdvOverrideAudit, bool>> predicate) =>
                    _stored.Where(predicate.Compile()).ToList());
            overrideAuditRepository
                .Setup(x => x.CreateAsync(It.IsAny<PdvOverrideAudit>()))
                .ReturnsAsync((PdvOverrideAudit audit) =>
                {
                    _stored.Add(audit);
                    return audit;
                });
            overrideAuditRepository.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict).Object;

            return new PdvController(
                new Mock<IPdvService>(MockBehavior.Strict).Object,
                new Mock<IRepository<PdvTerminal>>(MockBehavior.Strict).Object,
                userRepository,
                overrideAuditRepository.Object,
                new Mock<IRepository<Enterprise>>().Object,
                new Mock<INFeService<NFeDocument>>().Object,
                FiscalSettings(),
                new PasswordLoginService(userRepository),
                new TokenService(new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object, configuration))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(claims ??
                        [
                            new Claim(ClaimTypes.GroupSid, _enterpriseId.ToString()),
                            new Claim(ClaimTypes.Sid, _operatorUserId.ToString())
                        ], "Test"))
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
