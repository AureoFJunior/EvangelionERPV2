using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Security;
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
    /// What a till refreshes on every sync: who may do what, and who may no longer do anything.
    /// </summary>
    public class PdvControllerSyncStateTests
    {
        private readonly Guid _callerEnterpriseId = Guid.NewGuid();
        private readonly List<PdvOverrideAudit> _failedOverrides = [];

        /// <summary>The calling tenant, as GetSyncState reads it back for the PIX flag.</summary>
        private Enterprise _enterprise = new() { Id = Guid.NewGuid(), Name = "Mercado Evangelion" };

        /// <summary>The tenant's fiscal profile, as GetSyncState reads it for the NFC-e flag.</summary>
        private EnterpriseFiscalSettings _fiscal = new() { Enabled = true, NFCeEnabled = true };

        [Fact]
        public void GetSyncState_RequiresATillGrant()
        {
            ControllerPolicyTestHelper.AssertActionPolicy<PdvController>(
                nameof(PdvController.GetSyncState),
                "rbac:" + RbacPermissions.Pdv.Operate);
        }

        [Fact]
        public async Task GetSyncState_SaysPixIsOff_WhenTheShopHasNoStripeAccount()
        {
            _enterprise.PixPaymentsEnabled = true;
            _enterprise.StripeConnectedAccountId = null;

            var dto = await Read(CreateController());

            Assert.False(dto.PixEnabled);
        }

        [Fact]
        public async Task GetSyncState_SaysPixIsOff_WhenTheAccountIsNotThroughTheInvite()
        {
            _enterprise.StripeConnectedAccountId = "acct_123";
            _enterprise.PixPaymentsEnabled = false;

            var dto = await Read(CreateController());

            Assert.False(dto.PixEnabled);
        }

        [Fact]
        public async Task GetSyncState_SaysPixIsOn_WhenTheShopHasBoth()
        {
            _enterprise.StripeConnectedAccountId = "acct_123";
            _enterprise.PixPaymentsEnabled = true;

            var dto = await Read(CreateController());

            Assert.True(dto.PixEnabled);
        }

        [Fact]
        public async Task GetSyncState_ListsRevokedUsers_SoTheTillCanPurgeThem()
        {
            var sacked = BuildUser(EnumAccessLevel.Supervisor, pin: "482913", isActive: false);
            var controller = CreateController(sacked);

            var dto = await Read(controller);

            Assert.Contains(sacked.Id, dto.RevokedUserIds);
            Assert.DoesNotContain(dto.Operators, op => op.UserId == sacked.Id);
        }

        [Fact]
        public async Task GetSyncState_IsScopedToTheCallersEnterprise()
        {
            var ours = BuildUser(EnumAccessLevel.Supervisor, pin: "482913");
            var theirs = BuildUser(EnumAccessLevel.Supervisor, pin: "111111", enterpriseId: Guid.NewGuid());
            var theirSacked = BuildUser(EnumAccessLevel.Employee, isActive: false, enterpriseId: theirs.EnterpriseId);

            var controller = CreateController(ours, theirs, theirSacked);

            var dto = await Read(controller);

            Assert.All(dto.Operators, op => Assert.Equal(_callerEnterpriseId, op.EnterpriseId));
            Assert.DoesNotContain(theirSacked.Id, dto.RevokedUserIds);
        }

        [Fact]
        public async Task GetSyncState_SendsAVerifierOnlyForSupervisors()
        {
            var supervisor = BuildUser(EnumAccessLevel.Supervisor, pin: "482913");
            var cashier = BuildUser(EnumAccessLevel.Employee, pin: "111111");

            var controller = CreateController(supervisor, cashier);

            var dto = await Read(controller);

            Assert.Equal(supervisor.PdvPinVerifier, Assert.Single(dto.Operators, op => op.UserId == supervisor.Id).PinVerifier);
            Assert.Null(Assert.Single(dto.Operators, op => op.UserId == cashier.Id).PinVerifier);
        }

        [Fact]
        public async Task GetSyncState_ListsOnlyTillPermissions()
        {
            var controller = CreateController(BuildUser(EnumAccessLevel.Supervisor, pin: "482913"));

            var dto = await Read(controller);

            Assert.All(dto.Operators, op =>
                Assert.All(op.Permissions, permission => Assert.StartsWith("pdv.", permission)));
        }

        [Fact]
        public async Task GetSyncState_ShipsRefusedOverridesFromEveryTerminal()
        {
            var supervisor = BuildUser(EnumAccessLevel.Supervisor, pin: "482913");
            var controller = CreateController(supervisor);

            _failedOverrides.Add(PdvOverrideAudit.Record(
                _callerEnterpriseId, Guid.NewGuid(), supervisor.Id, Guid.NewGuid(),
                "CAIXA-02", Guid.NewGuid(), RbacPermissions.Pdv.VoidItem,
                succeeded: false, occurredAt: DateTime.UtcNow));

            var dto = await Read(controller);

            Assert.Equal(supervisor.Id, Assert.Single(dto.FailedOverrides).SupervisorUserId);
        }

        [Fact]
        public async Task GetSyncState_ReturnsUnauthorized_WhenEnterpriseClaimIsMissing()
        {
            var controller = CreateController(users: []);
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([], "Test"));

            Assert.IsType<UnauthorizedResult>(await controller.GetSyncState());
        }

        [Fact]
        public async Task GetSyncState_CarriesTheUserNameTheTillLogsInWith()
        {
            var supervisor = BuildUser(EnumAccessLevel.Supervisor, pin: "482913");
            var controller = CreateController(supervisor);

            var dto = await Read(controller);

            Assert.Equal(supervisor.UserName, Assert.Single(dto.Operators).UserName);
        }

        private static async Task<PdvSyncStateDTO> Read(PdvController controller)
        {
            var ok = Assert.IsType<OkObjectResult>(await controller.GetSyncState());
            return Assert.IsType<PdvSyncStateDTO>(ok.Value);
        }

        private User BuildUser(
            EnumAccessLevel accessLevel,
            string? pin = null,
            bool isActive = true,
            Guid? enterpriseId = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                UserName = "caixa01",
                FirstName = "Caixa",
                LastName = "Um",
                Email = $"{Guid.NewGuid():N}@evangelion.com",
                BirthDate = DateTime.UtcNow.AddYears(-25),
                EnterpriseId = enterpriseId ?? _callerEnterpriseId,
                AccessLevel = (short)accessLevel,
                IsActive = isActive,
                PdvPinVerifier = pin == null ? null : Pbkdf2Verifier.Create(pin)
            };

        [Fact]
        public async Task GetSyncState_SaysNFCeIsOn_WhenBothOfTheTenantsSwitchesAre()
        {
            _fiscal = new EnterpriseFiscalSettings { Enabled = true, NFCeEnabled = true };

            var dto = await Read(CreateController());

            Assert.True(dto.NFCeEnabled);
        }

        [Fact]
        public async Task GetSyncState_SaysNFCeIsOff_WhenFiscalIssuanceIsOffAltogether()
        {
            _fiscal = new EnterpriseFiscalSettings { Enabled = false, NFCeEnabled = true };

            var dto = await Read(CreateController());

            Assert.False(dto.NFCeEnabled);
        }

        private PdvController CreateController(params User[] users)
        {
            var userRepository = new Mock<IRepository<User>>();
            userRepository
                .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<User, bool>>>()))
                .ReturnsAsync((int? _, int? __, Expression<Func<User, bool>> predicate) =>
                    users.Where(predicate.Compile()));

            var overrideAuditRepository = new Mock<IRepository<PdvOverrideAudit>>();
            overrideAuditRepository
                .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<PdvOverrideAudit, bool>>>()))
                .ReturnsAsync((int? _, int? __, Expression<Func<PdvOverrideAudit, bool>> predicate) =>
                    _failedOverrides.Where(predicate.Compile()));

            var enterpriseRepository = new Mock<IRepository<Enterprise>>();
            enterpriseRepository
                .Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync(() => _enterprise);

            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

            return new PdvController(
                new Mock<IPdvService>(MockBehavior.Strict).Object,
                new Mock<IRepository<PdvTerminal>>(MockBehavior.Strict).Object,
                userRepository.Object,
                overrideAuditRepository.Object,
                enterpriseRepository.Object,
                new Mock<INFeService<NFeDocument>>().Object,
                FiscalSettings(),
                new PasswordLoginService(userRepository.Object),
                new TokenService(new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object, configuration))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.GroupSid, _callerEnterpriseId.ToString())], "Test"))
                    }
                }
            };
        }

        private INFeFiscalSettingsService FiscalSettings()
        {
            var settings = new Mock<INFeFiscalSettingsService>();
            settings.Setup(x => x.GetSettingsAsync(It.IsAny<Guid>())).ReturnsAsync(() => _fiscal);

            return settings.Object;
        }
    }
}
