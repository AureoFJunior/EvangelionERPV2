using EvangelionERPV2.NFeModule.Application.Interface;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.PdvModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The till's login. Separate from <c>UserController.LogInto</c> on purpose: the web login path
    /// stays untouched, and the token this issues is scoped to <c>pdv.*</c> so a credential lifted
    /// off a shop floor cannot read payroll.
    /// </summary>
    [Collection(SharedFunctionsKeyCollection.Name)]
    public class PdvControllerLoginTests
    {
        private const string UserName = "caixa01";
        private const string Password = "correct-horse";
        /// <summary>
        /// One per test — xUnit builds a fresh instance for each. The login rate-limit counters are
        /// process-static, so a shared terminal id would make these tests each other's failures.
        /// </summary>
        private readonly string TerminalId = $"CAIXA-{Guid.NewGuid():N}"[..24];
        private const string Credential = "the-credential";

        [Fact]
        public async Task Login_IsAnonymous_BecauseATillHasNoTokenYet()
        {
            var login = typeof(PdvController).GetMethod(nameof(PdvController.Login))!;

            Assert.NotNull(login.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false).FirstOrDefault());
        }

        [Fact]
        public async Task Login_ReturnsUnauthorized_WhenTerminalHeaderIsMissing()
        {
            var enterpriseId = Guid.NewGuid();
            var controller = CreateController(enterpriseId, terminalHeader: null);

            var response = await controller.Login(new PdvLoginRequestDTO { UserName = UserName, Password = Password });

            Assert.IsType<UnauthorizedResult>(response);
        }

        [Fact]
        public async Task Login_ReturnsUnauthorized_WhenTheHeaderHasNoSeparator()
        {
            var enterpriseId = Guid.NewGuid();
            var controller = CreateController(enterpriseId, terminalHeader: TerminalId);

            var response = await controller.Login(new PdvLoginRequestDTO { UserName = UserName, Password = Password });

            Assert.IsType<UnauthorizedResult>(response);
        }

        [Fact]
        public async Task Login_ReturnsUnauthorized_WhenTheCredentialIsWrong()
        {
            var enterpriseId = Guid.NewGuid();
            var controller = CreateController(enterpriseId, $"{TerminalId}:not-the-credential");

            var response = await controller.Login(new PdvLoginRequestDTO { UserName = UserName, Password = Password });

            Assert.IsType<UnauthorizedResult>(response);
        }

        [Fact]
        public async Task Login_ReturnsUnauthorized_WhenTerminalIsRevoked()
        {
            var enterpriseId = Guid.NewGuid();
            var controller = CreateController(enterpriseId, $"{TerminalId}:{Credential}", revoked: true);

            var response = await controller.Login(new PdvLoginRequestDTO { UserName = UserName, Password = Password });

            Assert.IsType<UnauthorizedResult>(response);
        }

        [Fact]
        public async Task Login_ReturnsUnauthorized_WhenThePasswordIsWrong()
        {
            var enterpriseId = Guid.NewGuid();
            var controller = CreateController(enterpriseId, $"{TerminalId}:{Credential}");

            var response = await controller.Login(new PdvLoginRequestDTO { UserName = UserName, Password = "wrong" });

            Assert.IsType<UnauthorizedResult>(response);
        }

        [Fact]
        public async Task Login_ReturnsUnauthorized_WhenTheOperatorBelongsToAnotherEnterprise()
        {
            var controller = CreateController(
                Guid.NewGuid(),
                $"{TerminalId}:{Credential}",
                operatorEnterpriseId: Guid.NewGuid());

            var response = await controller.Login(new PdvLoginRequestDTO { UserName = UserName, Password = Password });

            Assert.IsType<UnauthorizedResult>(response);
        }

        [Fact]
        public async Task Login_ReturnsUnauthorized_WhenTheOperatorHasNoPdvGrants()
        {
            var enterpriseId = Guid.NewGuid();

            var controller = CreateController(enterpriseId, $"{TerminalId}:{Credential}", accessLevel: 99);

            var response = await controller.Login(new PdvLoginRequestDTO { UserName = UserName, Password = Password });

            Assert.IsType<UnauthorizedResult>(response);
        }

        [Fact]
        public async Task Login_FindsTheTerminal_WhenAnotherTenantSharesTheTerminalId()
        {
            var enterpriseId = Guid.NewGuid();
            var otherTenantsTerminal = PdvTerminal.Enroll(Guid.NewGuid(), TerminalId, "a-different-credential");

            var controller = CreateController(
                enterpriseId,
                $"{TerminalId}:{Credential}",
                extraTerminals: [otherTenantsTerminal]);

            var response = await controller.Login(new PdvLoginRequestDTO { UserName = UserName, Password = Password });

            Assert.IsType<OkObjectResult>(response);
        }

        [Fact]
        public async Task Login_IssuesTokenScopedToPdvPermissionsOnly()
        {
            var enterpriseId = Guid.NewGuid();
            var controller = CreateController(enterpriseId, $"{TerminalId}:{Credential}");

            var response = await controller.Login(new PdvLoginRequestDTO { UserName = UserName, Password = Password });

            var ok = Assert.IsType<OkObjectResult>(response);
            var dto = Assert.IsType<PdvLoginResponseDTO>(ok.Value);

            Assert.NotEmpty(dto.Permissions);
            Assert.All(dto.Permissions, permission => Assert.StartsWith("pdv.", permission));
            Assert.Equal(enterpriseId, dto.EnterpriseId);
            Assert.False(string.IsNullOrWhiteSpace(dto.Token));
            Assert.False(string.IsNullOrWhiteSpace(dto.RefreshToken));
        }

        [Fact]
        public async Task Login_StampsTheScopeClaimTheAuthorizationHandlersRead()
        {
            var enterpriseId = Guid.NewGuid();
            var controller = CreateController(enterpriseId, $"{TerminalId}:{Credential}");

            var response = await controller.Login(new PdvLoginRequestDTO { UserName = UserName, Password = Password });

            var dto = Assert.IsType<PdvLoginResponseDTO>(Assert.IsType<OkObjectResult>(response).Value);
            var token = new JwtSecurityTokenHandler().ReadJwtToken(dto.Token);

            Assert.Contains(token.Claims, claim =>
                claim.Type == PdvTokenScope.ClaimType && claim.Value == PdvTokenScope.ClaimValue);
            Assert.Contains(token.Claims, claim => claim.Value == enterpriseId.ToString());
        }

        [Fact]
        public void Login_DoesNotRequireRecaptcha()
        {
            Assert.Null(typeof(PdvLoginRequestDTO).GetProperty("RecaptchaToken"));
        }

        private PdvController CreateController(
            Guid terminalEnterpriseId,
            string? terminalHeader,
            bool revoked = false,
            Guid? operatorEnterpriseId = null,
            short accessLevel = (short)EnumAccessLevel.Employee,
            PdvTerminal[]? extraTerminals = null)
        {
            var terminal = PdvTerminal.Enroll(terminalEnterpriseId, TerminalId, Credential);
            terminal.IsRevoked = revoked;

            var terminalRepository = new Mock<IRepository<PdvTerminal>>();
            terminalRepository
                .Setup(x => x.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<PdvTerminal, bool>>>()))
                .ReturnsAsync([.. extraTerminals ?? [], terminal]);
            terminalRepository.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = UserName,
                Password = SharedFunctions.HashPassword(Password),
                Email = $"{Guid.NewGuid():N}@evangelion.com",
                FirstName = "Caixa",
                LastName = "Um",
                BirthDate = DateTime.UtcNow.AddYears(-25),
                EnterpriseId = operatorEnterpriseId ?? terminalEnterpriseId,
                AccessLevel = accessLevel,
                IsActive = true
            };

            var userRepository = new Mock<IRepository<User>>();
            userRepository
                .Setup(x => x.GetAllAsyncByFilter(
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<Expression<Func<User, bool>>>(),
                    It.IsAny<Expression<Func<User, object>>>()))
                .ReturnsAsync([user]);

            var configuration = EnsureSharedFunctionsInitialized();

            var refreshTokenRepository = new Mock<IRepository<RefreshToken>>();
            refreshTokenRepository
                .Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var controller = new PdvController(
                new Mock<IPdvService>(MockBehavior.Strict).Object,
                terminalRepository.Object,
                userRepository.Object,
                new Mock<IRepository<PdvOverrideAudit>>().Object,
                new Mock<IRepository<Enterprise>>().Object,
                new Mock<INFeService<NFeDocument>>().Object,
                FiscalSettings(),
                new PasswordLoginService(userRepository.Object),
                new TokenService(refreshTokenRepository.Object, configuration))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };

            if (terminalHeader != null)
                controller.ControllerContext.HttpContext.Request.Headers["X-Pdv-Terminal"] = terminalHeader;

            return controller;
        }

        /// <summary>
        /// <c>TokenService</c> signs with the process-wide key <c>SharedFunctions</c> resolves, so a
        /// test that reaches token generation has to stand that global up. The secret name is unique
        /// to this class because the provider's cache is process-static.
        /// </summary>
        private static IConfiguration EnsureSharedFunctionsInitialized()
        {
            const string secretName = "pdv-login-tests-signing-key";

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = secretName,
                    ["Encryption:TokenKey"] = secretName
                })
                .Build();

            var secretsManager = new Mock<IAmazonSecretsManager>(MockBehavior.Strict);
            secretsManager
                .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse { SecretString = Convert.ToBase64String(new byte[32]) });

            var kmsProvider = new AWSKMSKeyProvider(secretsManager.Object, configuration);

            SharedFunctions.Initialize(new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(kmsProvider)
                .BuildServiceProvider());

            return configuration;
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
