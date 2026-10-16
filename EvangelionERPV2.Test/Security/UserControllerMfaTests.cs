using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Services;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;

namespace EvangelionERPV2.Test.Security
{
    [Collection(SharedFunctionsKeyCollection.Name)]
    public class UserControllerMfaTests
    {
        private const string Password = "Password1!";
        private readonly User _user;
        private readonly Mock<IUserService<User>> _userService = new();
        private readonly Mock<IRepository<RefreshToken>> _refreshTokens = new();
        private readonly MemoryDistributedCache _cache = new(Options.Create(new MemoryDistributedCacheOptions()));
        private readonly MfaService _mfa;
        private readonly UserController _controller;
        private readonly Mock<IRepository<User>> _users = new();
        private readonly Mock<IPermissionEvaluator> _permissions = new();
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _services;
        private readonly Guid _enterpriseId = Guid.NewGuid();

        public UserControllerMfaTests()
        {
            var configuration = _configuration = SharedFunctionsKeyCollection.EnsureInitialized();
            var enterpriseId = _enterpriseId;
            _user = new User
            {
                Id = Guid.NewGuid(),
                UserName = "mfa.user",
                Password = SharedFunctions.HashPassword(Password),
                Email = "mfa.user@evangelion.com",
                FirstName = "Mfa",
                LastName = "User",
                EnterpriseId = enterpriseId,
                IsActive = true,
                AccessLevel = (short)EnumAccessLevel.Employee
            };

            var users = _users;
            users.Setup(x => x.GetAllAsyncByFilter(It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(),
                    It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<Expression<Func<User, object>>>()))
                .ReturnsAsync([_user]);
            users.Setup(x => x.GetByIdAsync(_user.Id)).ReturnsAsync(_user);
            users.Setup(x => x.Update(It.IsAny<User>())).Returns((User u) => u);
            users.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task<MfaChallengeResult?>>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task<MfaChallengeResult?>> op, CancellationToken _) => op());
            users.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task<IReadOnlyList<string>?>>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task<IReadOnlyList<string>?>> op, CancellationToken _) => op());
            users.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task<bool>> op, CancellationToken _) => op());

            var recoveryCodes = new List<MfaRecoveryCode>();
            var codes = new Mock<IRepository<MfaRecoveryCode>>();
            codes.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<MfaRecoveryCode, bool>>>()))
                .ReturnsAsync((Expression<Func<MfaRecoveryCode, bool>> p) => recoveryCodes.Where(p.Compile()).ToList());
            codes.Setup(x => x.CreateRangeAsync(It.IsAny<IEnumerable<MfaRecoveryCode>>()))
                .ReturnsAsync((IEnumerable<MfaRecoveryCode> e) => { recoveryCodes.AddRange(e); return e; });
            codes.Setup(x => x.DeleteRange(It.IsAny<IEnumerable<MfaRecoveryCode>>()))
                .Returns((IEnumerable<MfaRecoveryCode> e) => { var l = e.ToList(); recoveryCodes.RemoveAll(l.Contains); return l; });

            _refreshTokens.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _userService.Setup(x => x.Update(It.IsAny<User>())).Returns((User u) => u);
            _mfa = new MfaService(users.Object, codes.Object, new Mock<IRepository<RefreshToken>>().Object, new Mock<EvangelionERPV2.Shared.Auditing.IAuditTrailWriter>().Object, _cache);

            var enterprises = new Mock<IRepository<Enterprise>>();
            enterprises.Setup(x => x.GetByIdAsync(enterpriseId)).ReturnsAsync(new Enterprise { Id = enterpriseId, IsActive = true });
            _permissions.Setup(x => x.GrantedAsync(It.IsAny<IReadOnlyCollection<User>>()))
                .ReturnsAsync((IReadOnlyCollection<User> candidates) => (IReadOnlyDictionary<Guid, IReadOnlySet<string>>)candidates
                    .ToDictionary(user => user.Id, _ => (IReadOnlySet<string>)new HashSet<string>()));
            _permissions.Setup(x => x.DecideAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<User>(), It.IsAny<string>()))
                .ReturnsAsync(PermissionDecision.Denied);
            _services = new ServiceCollection().AddSingleton(enterprises.Object).AddSingleton(_permissions.Object).BuildServiceProvider();
            _controller = BuildController(_mfa);
        }

        private UserController BuildController(MfaService mfa, Guid? callerEnterpriseId = null, User? callerUser = null)
        {
            var configuration = _configuration;
            var users = _users;
            callerUser ??= _user;
            users.Setup(x => x.GetByIdAsync(callerUser.Id)).ReturnsAsync(callerUser);
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>().Object, configuration);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Sid, callerUser.Id.ToString()),
                new Claim(ClaimTypes.GroupSid, (callerEnterpriseId ?? _enterpriseId).ToString())
            }, "test"));
            return new UserController(
                _userService.Object,
                users.Object,
                new Mock<IMapper>().Object,
                configuration,
                kmsProvider,
                new TokenService(_refreshTokens.Object, configuration),
                new Mock<IEmailService<EmailStructure>>().Object,
                new RecaptchaVerifier(new HttpClient(), configuration, kmsProvider),
                new PasswordLoginService(users.Object),
                mfa, null!)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = _services, User = principal } }
            };
        }

        private async Task<byte[]> EnableMfaAsync()
        {
            var setup = await _mfa.BeginSetupAsync(_user);
            var secret = Totp.FromBase32(setup.Secret);
            var step = Totp.CurrentStep(DateTimeOffset.UtcNow);
            Assert.NotNull(await _mfa.ConfirmSetupAsync(_user.Id, Totp.Code(secret, step)));
            _user.MfaLastUsedStep = step - 2;
            return secret;
        }

        private static string CurrentCode(byte[] secret) => Totp.Code(secret, Totp.CurrentStep(DateTimeOffset.UtcNow));

        private async Task<string> LoginForChallengeAsync()
        {
            var result = await _controller.LogInto(new LoginRequestDTO { UserName = _user.UserName, Password = Password });
            var challenge = Assert.IsType<MfaChallengeResponse>(Assert.IsType<OkObjectResult>(result).Value);
            return challenge.MfaToken;
        }

        [Fact]
        public async Task LogInto_WithMfaOn_ReturnsAChallengeAndNoTokens()
        {
            await EnableMfaAsync();

            var result = await _controller.LogInto(new LoginRequestDTO { UserName = _user.UserName, Password = Password });

            var challenge = Assert.IsType<MfaChallengeResponse>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.True(challenge.MfaRequired);
            Assert.False(string.IsNullOrWhiteSpace(challenge.MfaToken));
            _refreshTokens.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()), Times.Never);
            _userService.Verify(x => x.Update(It.IsAny<User>()), Times.Never);
            Assert.Null(_user.IsLogged);
        }

        [Fact]
        public async Task LogInto_WithMfaOff_StillIssuesTokens()
        {
            var result = await _controller.LogInto(new LoginRequestDTO { UserName = _user.UserName, Password = Password });

            var dto = Assert.IsType<UserDTO>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.False(string.IsNullOrEmpty(dto.Token));
            Assert.False(dto.MfaEnabled);
        }

        [Fact]
        public async Task VerifyMfa_WithValidCode_IssuesTheSameTokensALoginWould()
        {
            var secret = await EnableMfaAsync();
            var mfaToken = await LoginForChallengeAsync();

            var result = await _controller.VerifyMfa(new UserController.VerifyMfaRequest { MfaToken = mfaToken, Code = CurrentCode(secret) });

            var dto = Assert.IsType<UserDTO>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.False(string.IsNullOrEmpty(dto.Token));
            Assert.False(string.IsNullOrEmpty(dto.RefreshToken));
            Assert.True(dto.MfaEnabled);
            Assert.Null(dto.RecoveryCodesRemaining);
            Assert.Equal((short)1, _user.IsLogged);
        }

        [Fact]
        public async Task VerifyMfa_RejectsInitialPasswordChallengeAfterAnAdministratorReset()
        {
            var secret = await EnableMfaAsync();
            var challenge = await _mfa.CreateChallengeAsync(
                _user.Id,
                SharedFunctions.HashPassword("Pending9Password"),
                _user.Password);

            _user.Password = SharedFunctions.HashPassword("AdminReset9Password");

            Assert.Null(await _mfa.VerifyChallengeAsync(challenge, CurrentCode(secret)));
        }

        [Fact]
        public async Task VerifyMfa_DoesNotCommitAnInitialPasswordAfterAConcurrentReset()
        {
            var secret = await EnableMfaAsync();
            _user.MustChangePassword = true;
            var pendingHash = SharedFunctions.HashPassword("Pending9Password");
            var challenge = await _mfa.CreateChallengeAsync(_user.Id, pendingHash, _user.Password);
            _userService.Setup(x => x.TryChangeInitialPasswordAsync(_user.Id, _user.Password, pendingHash))
                .ReturnsAsync((User?)null);

            var result = await _controller.VerifyMfa(new UserController.VerifyMfaRequest
            {
                MfaToken = challenge,
                Code = CurrentCode(secret)
            });

            Assert.IsType<UnauthorizedResult>(result);
            Assert.True(_user.MustChangePassword);
            _refreshTokens.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task VerifyMfa_WithRecoveryCode_ReportsHowManyAreLeft()
        {
            await _mfa.BeginSetupAsync(_user);
            var secret = Totp.FromBase32(SharedFunctions.Decrypt(_user.MfaPendingSecret!));
            var recovery = await _mfa.ConfirmSetupAsync(_user.Id, CurrentCode(secret));
            var mfaToken = await LoginForChallengeAsync();

            var result = await _controller.VerifyMfa(new UserController.VerifyMfaRequest { MfaToken = mfaToken, RecoveryCode = recovery![0] });

            Assert.Equal(9, Assert.IsType<UserDTO>(Assert.IsType<OkObjectResult>(result).Value).RecoveryCodesRemaining);
        }

        [Fact]
        public async Task VerifyMfa_WithWrongCode_IsUnauthorized()
        {
            var secret = await EnableMfaAsync();
            var mfaToken = await LoginForChallengeAsync();
            var wrong = Totp.Code(secret, Totp.CurrentStep(DateTimeOffset.UtcNow) + 10);

            var result = await _controller.VerifyMfa(new UserController.VerifyMfaRequest { MfaToken = mfaToken, Code = wrong });

            Assert.IsType<UnauthorizedResult>(result);
            _refreshTokens.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData(null, "123456")]
        [InlineData("token", null)]
        [InlineData("token", " ")]
        public async Task VerifyMfa_WithoutTokenOrCode_IsBadRequest(string? mfaToken, string? code)
        {
            var result = await _controller.VerifyMfa(new UserController.VerifyMfaRequest { MfaToken = mfaToken, Code = code });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task LoginWithGoogle_WithMfaOn_ReturnsAChallengeAndNoTokens()
        {
            await EnableMfaAsync();
            _userService.Setup(x => x.LoginToSSOAsync("google-id-token")).ReturnsAsync(_user);

            var result = await _controller.LoginWithGoogle(JsonSerializer.SerializeToElement(new { idToken = "google-id-token" }));

            var challenge = Assert.IsType<MfaChallengeResponse>(Assert.IsType<OkObjectResult>(result).Value);
            Assert.True(challenge.MfaRequired);
            _refreshTokens.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task LoginWithGoogle_WhenInitialPasswordChangeIsRequired_DoesNotIssueAChallengeOrTokens()
        {
            _user.MustChangePassword = true;
            _userService.Setup(x => x.LoginToSSOAsync("google-id-token")).ReturnsAsync(_user);
            var result = await _controller.LoginWithGoogle(JsonSerializer.SerializeToElement(new { idToken = "google-id-token" }));
            var response = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
            Assert.Equal("password_change_required", JsonSerializer.SerializeToElement(response.Value).GetProperty("code").GetString());
            _refreshTokens.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task VerifyMfa_WhenInitialPasswordChangeBecomesRequired_DoesNotIssueTokens()
        {
            var secret = await EnableMfaAsync();
            var mfaToken = await LoginForChallengeAsync();
            _user.MustChangePassword = true;
            var result = await _controller.VerifyMfa(new UserController.VerifyMfaRequest { MfaToken = mfaToken, Code = CurrentCode(secret) });
            var response = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
            Assert.Equal("password_change_required", JsonSerializer.SerializeToElement(response.Value).GetProperty("code").GetString());
            _refreshTokens.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task LogInto_WhenTheChallengeCannotBeStored_Answers503WithoutTokens()
        {
            await EnableMfaAsync();
            var brokenCache = new Mock<IDistributedCache>();
            brokenCache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("redis down"));
            var controller = BuildController(new MfaService(_users.Object, new Mock<IRepository<MfaRecoveryCode>>().Object, new Mock<IRepository<RefreshToken>>().Object, new Mock<EvangelionERPV2.Shared.Auditing.IAuditTrailWriter>().Object, brokenCache.Object));

            var result = await controller.LogInto(new LoginRequestDTO { UserName = _user.UserName, Password = Password });

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeResult>(result).StatusCode);
            _refreshTokens.Verify(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Theory]
        [InlineData("12345")]
        [InlineData("abcdef")]
        [InlineData("ABCDE-FGH1J")]
        public async Task VerifyMfa_WithAMalformedCode_IsBadRequestAndSpendsNoAttempt(string malformed)
        {
            var secret = await EnableMfaAsync();
            var mfaToken = await LoginForChallengeAsync();

            for (var i = 0; i < 5; i++)
                Assert.IsType<BadRequestObjectResult>(await _controller.VerifyMfa(new UserController.VerifyMfaRequest { MfaToken = mfaToken, Code = malformed }));

            var result = await _controller.VerifyMfa(new UserController.VerifyMfaRequest { MfaToken = mfaToken, Code = CurrentCode(secret) });
            Assert.IsType<UserDTO>(Assert.IsType<OkObjectResult>(result).Value);
        }

        [Fact]
        public async Task BeginMfaSetup_WhenAlreadyOn_IsConflict()
        {
            await EnableMfaAsync();

            Assert.IsType<ConflictObjectResult>(await _controller.BeginMfaSetup());
        }

        [Fact]
        public async Task ResetMfa_InTheSameEnterprise_TurnsItOff()
        {
            await EnableMfaAsync();

            Assert.IsType<NoContentResult>(await _controller.ResetMfa(_user.Id));
            Assert.False(_user.MfaEnabled);
            Assert.Null(_user.MfaSecret);
        }

        [Fact]
        public async Task ResetMfa_ForAUserOfAnotherEnterprise_IsNotFoundAndChangesNothing()
        {
            await EnableMfaAsync();
            var outsider = BuildController(_mfa, callerEnterpriseId: Guid.NewGuid());

            Assert.IsType<NotFoundResult>(await outsider.ResetMfa(_user.Id));
            Assert.True(_user.MfaEnabled);
        }

        [Fact]
        public async Task ResetMfa_ManagerCannotResetAnAccessManagersMfa()
        {
            await EnableMfaAsync();
            _user.AccessLevel = (short)EnumAccessLevel.Admin;
            var manager = new User
            {
                Id = Guid.NewGuid(), UserName = "manager", AccessLevel = (short)EnumAccessLevel.Manager,
                EnterpriseId = _enterpriseId, IsActive = true
            };
            var controller = BuildController(_mfa, callerUser: manager);

            var result = Assert.IsType<ObjectResult>(await controller.ResetMfa(_user.Id));

            Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
            Assert.True(_user.MfaEnabled);
            _permissions.Verify(x => x.DecideAsync(It.IsAny<ClaimsPrincipal>(), manager, RbacPermissions.Iam.Manage), Times.Once);
        }

        [Fact]
        public async Task ResetMfa_ManagerCannotResetAPeerManagersMfaWithoutIamManage()
        {
            await EnableMfaAsync();
            _user.AccessLevel = (short)EnumAccessLevel.Manager;
            var manager = new User
            {
                Id = Guid.NewGuid(), UserName = "peer-manager", AccessLevel = (short)EnumAccessLevel.Manager,
                EnterpriseId = _enterpriseId, IsActive = true
            };
            var controller = BuildController(_mfa, callerUser: manager);

            var result = await controller.ResetMfa(_user.Id);

            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
            Assert.True(_user.MfaEnabled);
            _permissions.Verify(x => x.DecideAsync(It.IsAny<ClaimsPrincipal>(), manager, RbacPermissions.Iam.Manage), Times.Once);
        }

        [Theory]
        [InlineData("VerifyMfa", 10)]
        [InlineData("ConfirmMfaSetup", 5)]
        [InlineData("DisableMfa", 5)]
        [InlineData("RegenerateRecoveryCodes", 5)]
        [InlineData("RefreshToken", 30)]
        [InlineData("LoginWithApple", 5)]
        [InlineData("LinkApple", 5)]
        public void CodeCheckingEndpoints_HaveTheirOwnRateLimitRuleInProduction(string action, int limit)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EvangelionERPV2.sln")))
                dir = dir.Parent;
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "EvangelionERPV2.Web", "appsettings.Production.json")));

            var rule = json.RootElement.GetProperty("IpRateLimiting").GetProperty("GeneralRules").EnumerateArray()
                .Single(r => r.GetProperty("Endpoint").GetString() == $"*:/api/v1/User/{action}");
            Assert.Equal("1m", rule.GetProperty("Period").GetString());
            Assert.Equal(limit, rule.GetProperty("Limit").GetInt32());
        }

        [Fact]
        public async Task VerifyMfa_ForAUserDeactivatedAfterThePassword_IsUnauthorized()
        {
            var secret = await EnableMfaAsync();
            var mfaToken = await LoginForChallengeAsync();
            _user.IsActive = false;

            var result = await _controller.VerifyMfa(new UserController.VerifyMfaRequest { MfaToken = mfaToken, Code = CurrentCode(secret) });

            Assert.IsType<UnauthorizedResult>(result);
        }
    }

    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class SharedFunctionsKeyCollection
    {
        public const string Name = "SharedFunctions encryption key";

        private SharedFunctionsKeyCollection() { }

        public static IConfiguration EnsureInitialized()
        {
            const string secretName = "shared-functions-tests-key";
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

            SharedFunctions.Initialize(new ServiceCollection()
                .AddSingleton<IConfiguration>(configuration)
                .AddSingleton(new AWSKMSKeyProvider(secretsManager.Object, configuration))
                .BuildServiceProvider());

            return configuration;
        }
    }
}
