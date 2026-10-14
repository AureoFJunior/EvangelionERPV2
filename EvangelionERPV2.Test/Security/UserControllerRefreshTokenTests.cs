using Amazon.SecretsManager;
using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace EvangelionERPV2.Test.Security
{
    // BIO-01: RefreshToken keeps a session alive past the access token; Logout ends it.
    [Collection(SharedFunctionsKeyCollection.Name)]
    public class UserControllerRefreshTokenTests
    {
        private readonly List<RefreshToken> _tokens = new();
        private readonly User _user;
        private readonly Enterprise _enterprise;
        private readonly UserController _controller;

        public UserControllerRefreshTokenTests()
        {
            var configuration = SharedFunctionsKeyCollection.EnsureInitialized();
            _enterprise = new Enterprise { Id = Guid.NewGuid(), IsActive = true };
            _user = new User { Id = Guid.NewGuid(), UserName = "ana", Email = "ana@evangelion.com", FirstName = "Ana", LastName = "Lima", EnterpriseId = _enterprise.Id, IsActive = true };

            var refreshTokens = new Mock<IRepository<RefreshToken>>();
            refreshTokens.Setup(r => r.GetAllAsyncByFilter(It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(),
                    It.IsAny<Expression<Func<RefreshToken, bool>>>(), It.IsAny<Expression<Func<RefreshToken, object>>>()))
                .ReturnsAsync((bool _, int? _, int? size, Expression<Func<RefreshToken, bool>> p, Expression<Func<RefreshToken, object>> _) =>
                    _tokens.Where(p.Compile()).Take(size ?? int.MaxValue).ToList());
            refreshTokens.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task> op, CancellationToken _) => op());
            refreshTokens.Setup(r => r.Update(It.IsAny<RefreshToken>())).Returns((RefreshToken t) => t);
            refreshTokens.Setup(r => r.CreateAsync(It.IsAny<RefreshToken>())).ReturnsAsync((RefreshToken t) => { _tokens.Add(t); return t; });

            var users = new Mock<IRepository<User>>();
            users.Setup(r => r.GetByIdAsync(_user.Id)).ReturnsAsync(_user);
            var enterprises = new Mock<IRepository<Enterprise>>();
            enterprises.Setup(r => r.GetByIdAsync(_enterprise.Id)).ReturnsAsync(_enterprise);

            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>().Object, configuration);
            var caller = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Sid, _user.Id.ToString()) }, "test"));
            _controller = new UserController(
                new Mock<IUserService<User>>().Object,
                users.Object,
                new Mock<IMapper>().Object,
                configuration,
                kmsProvider,
                new TokenService(refreshTokens.Object, configuration),
                new Mock<IEmailService<EmailStructure>>().Object,
                new RecaptchaVerifier(new HttpClient(), configuration, kmsProvider),
                new PasswordLoginService(users.Object),
                null!, null!)
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

        private void Issue(string raw, string kind = RefreshTokenKind.Session) => _tokens.Add(new RefreshToken
        {
            Kind = kind,
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            TokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw))),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsActive = true
        });

        private Task<IActionResult> Refresh(string? raw) =>
            _controller.RefreshToken(new UserController.RefreshTokenRequest { RefreshToken = raw });

        [Fact]
        public async Task RefreshToken_WithALiveToken_ReturnsANewAccessAndRefreshToken()
        {
            Issue("phone-token");

            var dto = Assert.IsType<UserDTO>(Assert.IsType<OkObjectResult>(await Refresh("phone-token")).Value);

            Assert.False(string.IsNullOrEmpty(dto.Token));
            Assert.False(string.IsNullOrEmpty(dto.RefreshToken));
            Assert.NotEqual("phone-token", dto.RefreshToken);
            Assert.Equal(_user.Id, dto.Id);
            Assert.IsType<OkObjectResult>(await Refresh(dto.RefreshToken));
        }

        [Fact]
        public async Task RefreshToken_ASpentToken_IsUnauthorizedAndEndsTheSession()
        {
            Issue("phone-token");
            var dto = (UserDTO)((OkObjectResult)await Refresh("phone-token")).Value!;

            Assert.IsType<UnauthorizedResult>(await Refresh("phone-token"));
            Assert.IsType<UnauthorizedResult>(await Refresh(dto.RefreshToken));
        }

        [Fact]
        public async Task RefreshToken_ForADeactivatedUser_IsUnauthorizedAndLeavesNoLiveToken()
        {
            Issue("phone-token");
            _user.IsActive = false;

            Assert.IsType<UnauthorizedResult>(await Refresh("phone-token"));
            Assert.DoesNotContain(_tokens, t => t.IsActive == true && t.RevokedAt == null);
        }

        [Fact]
        public async Task RefreshToken_ForAnInactiveEnterprise_IsUnauthorized()
        {
            Issue("phone-token");
            _enterprise.IsActive = false;

            Assert.IsType<UnauthorizedResult>(await Refresh("phone-token"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(" ")]
        public async Task RefreshToken_WithoutAToken_IsBadRequest(string? raw)
        {
            Assert.IsType<BadRequestObjectResult>(await Refresh(raw));
        }

        [Fact]
        public async Task RefreshToken_TooLong_IsBadRequest()
        {
            Assert.IsType<BadRequestObjectResult>(await Refresh(new string('a', 257)));
        }

        [Fact]
        public async Task RefreshToken_Unknown_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Refresh("never-issued"));
        }

        [Fact]
        public async Task Logout_RevokesTheCallersRefreshToken()
        {
            Issue("phone-token");

            Assert.IsType<NoContentResult>(await _controller.Logout(new UserController.RefreshTokenRequest { RefreshToken = "phone-token" }));

            Assert.DoesNotContain(_tokens, t => t.IsActive == true && t.RevokedAt == null);
            Assert.IsType<UnauthorizedResult>(await Refresh("phone-token"));
        }

        [Fact]
        public async Task RefreshToken_FromATillSignIn_CannotBecomeAnErpSession()
        {
            Issue("till-token", RefreshTokenKind.Till);

            Assert.IsType<UnauthorizedResult>(await Refresh("till-token"));
        }
}
}
