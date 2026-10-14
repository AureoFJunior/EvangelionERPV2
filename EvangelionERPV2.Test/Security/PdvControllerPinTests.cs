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
    /// The override PIN, and the verifiers a till caches so an override still works with the
    /// network down.
    /// </summary>
    public class PdvControllerPinTests
    {
        [Fact]
        public void GetSupervisorVerifiers_RequiresATillGrant()
        {
            ControllerPolicyTestHelper.AssertActionPolicy<PdvController>(
                nameof(PdvController.GetSupervisorVerifiers),
                "rbac:" + RbacPermissions.Pdv.Operate);
        }

        [Fact]
        public async Task SetPdvPin_StoresOnlyAVerifier()
        {
            var user = BuildUser(EnumAccessLevel.Supervisor);
            var controller = CreateController([user], ClaimsFor(user));

            var response = await controller.SetPdvPin(new SetPdvPinRequestDTO { Pin = "482913" });

            Assert.IsType<NoContentResult>(response);
            Assert.NotNull(user.PdvPinVerifier);

            Assert.DoesNotContain("482913", user.PdvPinVerifier!);
            Assert.True(Pbkdf2Verifier.Verify("482913", user.PdvPinVerifier));
        }

        [Fact]
        public async Task SetPdvPin_IsRefusedToATillToken()
        {
            var user = BuildUser(EnumAccessLevel.Supervisor);
            var claims = ClaimsFor(user)
                .Append(new Claim(PdvTokenScope.ClaimType, PdvTokenScope.ClaimValue))
                .ToArray();

            var controller = CreateController([user], claims);

            var response = await controller.SetPdvPin(new SetPdvPinRequestDTO { Pin = "482913" });

            Assert.IsType<ForbidResult>(response);
            Assert.Null(user.PdvPinVerifier);
        }

        [Theory]
        [InlineData("12345")]
        [InlineData("48291a")]
        [InlineData("")]
        [InlineData("1234567890123")]
        public async Task SetPdvPin_RejectsAPinThatIsNotSixToTwelveDigits(string pin)
        {
            var user = BuildUser(EnumAccessLevel.Supervisor);
            var controller = CreateController([user], ClaimsFor(user));

            Assert.IsType<BadRequestObjectResult>(await controller.SetPdvPin(new SetPdvPinRequestDTO { Pin = pin }));
            Assert.Null(user.PdvPinVerifier);
        }

        [Fact]
        public async Task SetPdvPin_RefusesACallerFromAnotherEnterprise()
        {
            var user = BuildUser(EnumAccessLevel.Supervisor);
            var controller = CreateController([user], [
                new Claim(ClaimTypes.Sid, user.Id.ToString()),
                new Claim(ClaimTypes.GroupSid, Guid.NewGuid().ToString())
            ]);

            Assert.IsType<UnauthorizedResult>(await controller.SetPdvPin(new SetPdvPinRequestDTO { Pin = "482913" }));
            Assert.Null(user.PdvPinVerifier);
        }

        [Fact]
        public async Task SetPdvPin_RefusesADeactivatedUser()
        {
            var user = BuildUser(EnumAccessLevel.Supervisor);
            user.IsActive = false;

            var controller = CreateController([user], ClaimsFor(user));

            Assert.IsType<UnauthorizedResult>(await controller.SetPdvPin(new SetPdvPinRequestDTO { Pin = "482913" }));
            Assert.Null(user.PdvPinVerifier);
        }

        [Fact]
        public async Task GetSupervisorVerifiers_ListsSupervisors_ButNotOperators()
        {
            var supervisor = BuildUser(EnumAccessLevel.Supervisor, pin: "482913");
            var cashier = BuildUser(EnumAccessLevel.Employee, pin: "111111", enterpriseId: supervisor.EnterpriseId);

            var controller = CreateController([supervisor, cashier], ClaimsFor(cashier));

            var ok = Assert.IsType<OkObjectResult>(await controller.GetSupervisorVerifiers());
            var verifiers = Assert.IsAssignableFrom<IEnumerable<SupervisorVerifierDTO>>(ok.Value).ToList();

            var listed = Assert.Single(verifiers);
            Assert.Equal(supervisor.Id, listed.UserId);
            Assert.Equal(supervisor.PdvPinVerifier, listed.PinVerifier);
            Assert.All(listed.Permissions, permission => Assert.StartsWith("pdv.", permission));
        }

        [Fact]
        public async Task GetSupervisorVerifiers_ReturnsUnauthorized_WhenEnterpriseClaimIsMissing()
        {
            var controller = CreateController([], []);

            Assert.IsType<UnauthorizedResult>(await controller.GetSupervisorVerifiers());
        }

        private static Claim[] ClaimsFor(User user) =>
        [
            new Claim(ClaimTypes.Sid, user.Id.ToString()),
            new Claim(ClaimTypes.GroupSid, user.EnterpriseId!.Value.ToString())
        ];

        private static User BuildUser(EnumAccessLevel accessLevel, string? pin = null, Guid? enterpriseId = null) =>
            new()
            {
                Id = Guid.NewGuid(),
                UserName = "caixa01",
                FirstName = "Caixa",
                LastName = "Um",
                Email = $"{Guid.NewGuid():N}@evangelion.com",
                BirthDate = DateTime.UtcNow.AddYears(-25),
                EnterpriseId = enterpriseId ?? Guid.NewGuid(),
                AccessLevel = (short)accessLevel,
                IsActive = true,
                PdvPinVerifier = pin == null ? null : Pbkdf2Verifier.Create(pin)
            };

        private static PdvController CreateController(User[] users, Claim[] claims)
        {
            var userRepository = new Mock<IRepository<User>>();
            userRepository
                .Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => users.FirstOrDefault(user => user.Id == id)!);
            userRepository
                .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<User, bool>>>()))
                .ReturnsAsync((int? _, int? __, Expression<Func<User, bool>> predicate) =>
                    users.Where(predicate.Compile()));
            userRepository.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

            return new PdvController(
                new Mock<IPdvService>(MockBehavior.Strict).Object,
                new Mock<IRepository<PdvTerminal>>(MockBehavior.Strict).Object,
                userRepository.Object,
                new Mock<IRepository<PdvOverrideAudit>>().Object,
                new Mock<IRepository<Enterprise>>().Object,
                new Mock<INFeService<NFeDocument>>().Object,
                FiscalSettings(),
                new PasswordLoginService(userRepository.Object),
                new TokenService(new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object, configuration))
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
