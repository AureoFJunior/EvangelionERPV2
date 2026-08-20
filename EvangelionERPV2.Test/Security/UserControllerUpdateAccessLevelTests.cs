using System.Security.Claims;

using Amazon.SecretsManager;

using AutoMapper;

using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// <c>UpdateUser</c> validates the requested role with
    /// <c>Enum.IsDefined(typeof(EnumAccessLevel), ...)</c>, and that overload demands the enum's own
    /// underlying type. <c>UpdateUserRequestDTO.AccessLevel</c> is a <c>short</c> while
    /// <c>EnumAccessLevel</c> is backed by <c>int</c>, so passing it raw threw
    /// <c>ArgumentException</c> — on every call, whatever the role. The exception middleware turned
    /// that into a generic 400 and editing a user was impossible through the API.
    /// </summary>
    public class UserControllerUpdateAccessLevelTests
    {
        private static readonly Guid EnterpriseId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        [Theory]
        [InlineData(EnumAccessLevel.Admin)]
        [InlineData(EnumAccessLevel.Manager)]
        [InlineData(EnumAccessLevel.Supervisor)]
        [InlineData(EnumAccessLevel.Employee)]
        public async Task UpdateUser_WithAValidRole_Succeeds(EnumAccessLevel level)
        {
            var stored = BuildUser();
            var context = new ControllerContextBuilder(stored);

            var result = await context.Controller.UpdateUser(BuildRequest(stored.Id, (short)level));

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal((short)level, stored.AccessLevel);
        }

        [Fact]
        public async Task UpdateUser_WithARoleThatDoesNotExist_IsRefusedRatherThanThrowing()
        {
            var stored = BuildUser();
            var context = new ControllerContextBuilder(stored);

            var result = await context.Controller.UpdateUser(BuildRequest(stored.Id, 99));

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid access level.", badRequest.Value);
        }

        private static User BuildUser() => new()
        {
            Id = Guid.NewGuid(),
            FirstName = "Rei",
            LastName = "Ayanami",
            UserName = "rei",
            Email = "rei@nerv.jp",
            Password = SharedFunctions.HashPassword("Sup3rSecret!"),
            BirthDate = new DateTime(1995, 1, 1),
            AccessLevel = (short)EnumAccessLevel.Employee,
            EnterpriseId = EnterpriseId,
            IsActive = true
        };

        private static UpdateUserRequestDTO BuildRequest(Guid id, short accessLevel) => new()
        {
            Id = id,
            FirstName = "Rei",
            LastName = "Ayanami",
            UserName = "rei",
            Email = "rei@nerv.jp",
            BirthDate = new DateTime(1995, 1, 1),
            AccessLevel = accessLevel,
            ActualTheme = 0,
            Language = 0,
            Password = string.Empty,
            IsActive = true
        };

        private sealed class ControllerContextBuilder
        {
            public UserController Controller { get; }

            public ControllerContextBuilder(User stored)
            {
                var userRepository = new Mock<IRepository<User>>();
                userRepository.Setup(x => x.GetByIdAsync(stored.Id)).ReturnsAsync(stored);

                var userService = new Mock<IUserService<User>>();
                userService.Setup(x => x.Update(It.IsAny<User>())).Returns((User user) => user);

                var mapper = new Mock<IMapper>();
                mapper.Setup(x => x.Map<UserDTO>(It.IsAny<User>())).Returns(new UserDTO());

                var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
                var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>().Object, configuration);
                var tokenService = new TokenService(new Mock<IRepository<RefreshToken>>().Object, configuration);

                var httpContext = new DefaultHttpContext
                {
                    RequestServices = new ServiceCollection().BuildServiceProvider(),
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.GroupSid, EnterpriseId.ToString())
                    ], "test"))
                };

                Controller = new UserController(
                    userService.Object,
                    userRepository.Object,
                    mapper.Object,
                    configuration,
                    kmsProvider,
                    tokenService,
                    new Mock<IEmailService<EmailStructure>>().Object,
                    new RecaptchaVerifier(new HttpClient(), configuration, kmsProvider))
                {
                    ControllerContext = new ControllerContext { HttpContext = httpContext }
                };
            }
        }
    }
}
