using Amazon.SecretsManager;
using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using UserEntity = EvangelionERPV2.Shared.Entities.User;

namespace EvangelionERPV2.UserModule.Test.User
{
    // APL-02/03/05: Sign in with Apple on the controller, and linking it from the profile.
    [Collection(SharedFunctionsKeyCollection.Name)]
    public class UserControllerAppleTests
    {
        private const string Nonce = AppleTestTokens.RawNonce;
        private readonly AppleTestTokens _apple = new();
        private readonly List<UserEntity> _users = new();
        private readonly Mock<IRepository<RefreshToken>> _refreshTokens = new();
        private readonly Enterprise _enterprise = new() { Id = Guid.NewGuid(), IsActive = true };
        private readonly UserEntity _ana;
        private readonly UserController _controller;

        public UserControllerAppleTests()
        {
            var configuration = SharedFunctionsKeyCollection.EnsureInitialized();
            _ana = new UserEntity { Id = Guid.NewGuid(), UserName = "ana", Email = "ana@example.com", FirstName = "Ana", LastName = "Lima", EnterpriseId = _enterprise.Id, IsActive = true };
            _users.Add(_ana);

            var users = new Mock<IRepository<UserEntity>>();
            users.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<UserEntity, bool>>>()))
                .ReturnsAsync((Expression<Func<UserEntity, bool>> p) => _users.Where(p.Compile()).ToList());
            users.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id) => _users.Single(u => u.Id == id));
            users.Setup(r => r.Update(It.IsAny<UserEntity>())).Returns((UserEntity u) => u);
            _refreshTokens.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var enterprises = new Mock<IRepository<Enterprise>>();
            enterprises.Setup(r => r.GetByIdAsync(_enterprise.Id)).ReturnsAsync(_enterprise);
            var userService = new Mock<IUserService<UserEntity>>();
            userService.Setup(s => s.Update(It.IsAny<UserEntity>())).Returns((UserEntity u) => u);

            var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            var mfa = new MfaService(users.Object, new Mock<IRepository<MfaRecoveryCode>>().Object, new Mock<IRepository<RefreshToken>>().Object, new Mock<EvangelionERPV2.Shared.Auditing.IAuditTrailWriter>().Object, cache);
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>().Object, configuration);
            var caller = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Sid, _ana.Id.ToString()),
                new Claim(ClaimTypes.GroupSid, _enterprise.Id.ToString())
            }, "test"));

            _controller = new UserController(
                userService.Object,
                users.Object,
                new Mock<IMapper>().Object,
                configuration,
                kmsProvider,
                new TokenService(_refreshTokens.Object, configuration),
                new Mock<IEmailService<EmailStructure>>().Object,
                new RecaptchaVerifier(new HttpClient(), configuration, kmsProvider),
                new PasswordLoginService(users.Object),
                mfa,
                new AppleSignInService(_apple.Validator(), users.Object))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        RequestServices = new ServiceCollection().AddSingleton(enterprises.Object).BuildServiceProvider(),
                        User = caller
                    }
                }
            };
        }

        private Task<IActionResult> Login(string? token, string? nonce = Nonce) =>
            _controller.LoginWithApple(new UserController.AppleTokenRequest { IdentityToken = token, Nonce = nonce });

        [Fact]
        public async Task LoginWithApple_ForALinkedUser_IssuesTheSession()
        {
            _ana.AppleSubject = "001234.abcdef.0001";

            var dto = Assert.IsType<UserDTO>(Assert.IsType<OkObjectResult>(await Login(_apple.Token(email: null))).Value);

            Assert.Equal(_ana.Id, dto.Id);
            Assert.False(string.IsNullOrEmpty(dto.Token));
            Assert.True(dto.AppleLinked);
            Assert.Equal((short)1, _ana.IsLogged);
        }

        [Fact]
        public async Task LoginWithApple_ForAUserWithMfa_ReturnsTheChallengeAndNoTokens()
        {
            _ana.AppleSubject = "001234.abcdef.0001";
            _ana.MfaEnabled = true;

            var challenge = Assert.IsType<MfaChallengeResponse>(Assert.IsType<OkObjectResult>(await Login(_apple.Token())).Value);

            Assert.True(challenge.MfaRequired);
            _refreshTokens.Verify(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task LoginWithApple_WithNoErpUserBehindIt_IsUnauthorizedWithAppleNotLinked()
        {
            var result = Assert.IsType<UnauthorizedObjectResult>(await Login(_apple.Token(email: "x1y2@privaterelay.appleid.com")));

            Assert.Equal("apple_not_linked", JsonSerializer.SerializeToElement(result.Value).GetProperty("code").GetString());
        }

        [Fact]
        public async Task LoginWithApple_WithAForgedToken_IsUnauthorized()
        {
            _ana.AppleSubject = "001234.abcdef.0001";

            Assert.IsType<UnauthorizedResult>(await Login(_apple.Token(key: AppleTestTokens.NewKey("apple-key-1"))));
        }

        [Fact]
        public async Task LoginWithApple_ForAnInactiveEnterprise_IsUnauthorized()
        {
            _ana.AppleSubject = "001234.abcdef.0001";
            _enterprise.IsActive = false;

            Assert.IsType<UnauthorizedResult>(await Login(_apple.Token()));
        }

        [Theory]
        [InlineData(null, AppleTestTokens.RawNonce)]
        [InlineData("token", null)]
        [InlineData("token", "too-short")]
        public async Task LoginWithApple_WithAMalformedRequest_IsBadRequest(string? token, string? nonce)
        {
            Assert.IsType<BadRequestObjectResult>(await Login(token, nonce));
        }

        [Fact]
        public async Task LoginWithApple_WithAnOversizedToken_IsBadRequest()
        {
            Assert.IsType<BadRequestObjectResult>(await Login(new string('a', 4097)));
        }

        [Fact]
        public async Task LoginWithApple_WhenAppleKeysAreUnreachable_Answers503()
        {
            _apple.Keys.Fail = true;

            Assert.Equal(503, Assert.IsType<StatusCodeResult>(await Login(_apple.Token())).StatusCode);
        }

        [Fact]
        public async Task LinkApple_WhenAppleKeysAreUnreachable_Answers503AndLinksNothing()
        {
            _apple.Keys.Fail = true;

            var result = await _controller.LinkApple(new UserController.AppleTokenRequest { IdentityToken = _apple.Token(), Nonce = Nonce });

            Assert.Equal(503, Assert.IsType<StatusCodeResult>(result).StatusCode);
            Assert.Null(_ana.AppleSubject);
        }

        [Fact]
        public async Task LinkApple_StoresTheAppleIdOnTheCaller()
        {
            var result = await _controller.LinkApple(new UserController.AppleTokenRequest { IdentityToken = _apple.Token(), Nonce = Nonce });

            Assert.IsType<NoContentResult>(result);
            Assert.Equal("001234.abcdef.0001", _ana.AppleSubject);
        }

        [Fact]
        public async Task LinkApple_AnAppleIdAnotherUserHas_IsConflict()
        {
            _users.Add(new UserEntity { Id = Guid.NewGuid(), Email = "bia@example.com", AppleSubject = "001234.abcdef.0001", IsActive = true });

            var result = await _controller.LinkApple(new UserController.AppleTokenRequest { IdentityToken = _apple.Token(), Nonce = Nonce });

            Assert.IsType<ConflictObjectResult>(result);
            Assert.Null(_ana.AppleSubject);
        }

        [Fact]
        public async Task LinkApple_WithAnInvalidToken_IsBadRequest()
        {
            var result = await _controller.LinkApple(new UserController.AppleTokenRequest { IdentityToken = _apple.Token(), Nonce = "another-nonce-0123456789" });

            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Null(_ana.AppleSubject);
        }

        [Fact]
        public async Task UnlinkApple_ClearsTheCallersAppleId()
        {
            _ana.AppleSubject = "001234.abcdef.0001";

            Assert.IsType<NoContentResult>(await _controller.UnlinkApple());
            Assert.Null(_ana.AppleSubject);
        }
    }
}
