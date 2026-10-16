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
using System.Text.Json;

namespace EvangelionERPV2.Test.Security
{
    [Collection(SharedFunctionsKeyCollection.Name)]
    public class UserControllerInitialPasswordTests
    {
        private const string Temporary = "Temp0rary!";
        private const string Chosen = "MyOwn9Pass";
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly User _user;
        private readonly Mock<IUserService<User>> _userService = new();
        private readonly UserController _controller;

        public UserControllerInitialPasswordTests()
        {
            var configuration = SharedFunctionsKeyCollection.EnsureInitialized();
            _user = new User
            {
                Id = Guid.NewGuid(),
                UserName = "new.hire",
                Password = SharedFunctions.HashPassword(Temporary),
                Email = "new.hire@evangelion.com",
                EnterpriseId = _enterpriseId,
                MustChangePassword = true,
                IsActive = true
            };

            var users = new Mock<IRepository<User>>();
            users.Setup(x => x.GetAllAsyncByFilter(It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(),
                    It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<Expression<Func<User, object>>>()))
                .ReturnsAsync([_user]);
            users.Setup(x => x.GetByIdAsync(_user.Id)).ReturnsAsync(_user);

            var refreshTokens = new Mock<IRepository<RefreshToken>>();
            refreshTokens.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _userService.Setup(x => x.Update(It.IsAny<User>())).Returns((User u) => u);
            _userService.Setup(x => x.TryChangeInitialPasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns<Guid, string, string>((_, _, newHash) =>
                {
                    _user.Password = newHash;
                    _user.MustChangePassword = false;
                    _user.IsLogged = 1;
                    return Task.FromResult<User?>(_user);
                });

            var enterprises = new Mock<IRepository<Enterprise>>();
            enterprises.Setup(x => x.GetByIdAsync(_enterpriseId)).ReturnsAsync(new Enterprise { Id = _enterpriseId, IsActive = true });

            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>().Object, configuration);
            _controller = new UserController(
                _userService.Object,
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
                        RequestServices = new ServiceCollection().AddSingleton(enterprises.Object).BuildServiceProvider()
                    }
                }
            };
        }

        private static string? CodeOf(IActionResult result) =>
            result is ObjectResult { Value: { } value }
                ? JsonSerializer.SerializeToElement(value).TryGetProperty("code", out var code) ? code.GetString() : null
                : null;

        private Task<IActionResult> Change(string current = Temporary, string chosen = Chosen) =>
            _controller.ChangeInitialPassword(new UserController.InitialPasswordRequest
            {
                UserName = _user.UserName, CurrentPassword = current, NewPassword = chosen
            });

        [Fact]
        public async Task LoggingInWithAPasswordSomeoneElseChose_AsksForANewOneInsteadOfASession()
        {
            var result = await _controller.LogInto(new LoginRequestDTO { UserName = _user.UserName, Password = Temporary });

            Assert.Equal(403, ((ObjectResult)result).StatusCode);
            Assert.Equal("password_change_required", CodeOf(result));
            _userService.Verify(x => x.Update(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task ChoosingANewPassword_ReplacesTheTemporaryOne_AndSignsIn()
        {
            Assert.IsType<OkObjectResult>(await Change());

            Assert.False(_user.MustChangePassword);
            Assert.True(SharedFunctions.VerifyPassword(Chosen, _user.Password, out _));
            Assert.False(SharedFunctions.VerifyPassword(Temporary, _user.Password, out _));
        }

        [Fact]
        public async Task AfterTheChange_TheNewPasswordLogsInNormally()
        {
            await Change();

            var result = await _controller.LogInto(new LoginRequestDTO { UserName = _user.UserName, Password = Chosen });

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task TheWrongTemporaryPassword_ChangesNothing()
        {
            Assert.IsType<UnauthorizedResult>(await Change(current: "Wr0ng-guess"));
            Assert.True(_user.MustChangePassword);
        }

        [Fact]
        public async Task AConcurrentPasswordReset_PreventsTheStaleInitialPasswordChange()
        {
            _userService.Setup(x => x.TryChangeInitialPasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((User?)null);

            Assert.IsType<UnauthorizedResult>(await Change());

            Assert.True(_user.MustChangePassword);
            Assert.True(SharedFunctions.VerifyPassword(Temporary, _user.Password, out _));
        }

        [Fact]
        public async Task AnAccountThatChoseItsOwnPassword_CannotUseThisRoute()
        {
            _user.MustChangePassword = false;

            Assert.IsType<UnauthorizedResult>(await Change());
        }

        [Theory]
        [InlineData("short1A")]
        [InlineData("alllowercaseletters")]
        [InlineData(Temporary)]
        public async Task AWeakOrUnchangedPassword_IsRefused(string chosen)
        {
            Assert.IsType<BadRequestObjectResult>(await Change(chosen: chosen));
            Assert.True(_user.MustChangePassword);
        }
    }
}
