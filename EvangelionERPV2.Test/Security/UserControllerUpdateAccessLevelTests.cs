using System.Security.Claims;

using Amazon.SecretsManager;

using AutoMapper;

using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Auditing;
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

        [Fact]
        public async Task UpdateUser_ManagerCannotPromoteUserToAdmin()
        {
            var target = BuildUser();
            var context = new ControllerContextBuilder(target, EnumAccessLevel.Manager);

            var result = await context.Controller.UpdateUser(BuildRequest(target.Id, (short)EnumAccessLevel.Admin));

            Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, ((ObjectResult)result).StatusCode);
            context.UserService.Verify(x => x.Update(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUser_ManagerCannotChangePeerProfileOrPasswordWithoutIamManage()
        {
            var target = BuildUser();
            target.AccessLevel = (short)EnumAccessLevel.Manager;
            target.Password = "existing-password";
            var context = new ControllerContextBuilder(target, EnumAccessLevel.Manager, iamDecision: PermissionDecision.Denied);
            var request = BuildRequest(target.Id, (short)EnumAccessLevel.Manager);
            request.Password = "take-over-password";

            var result = await context.Controller.UpdateUser(request);

            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
            Assert.Equal("existing-password", target.Password);
            context.UserService.Verify(x => x.Update(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUser_OperatorPasswordResetRequiresHumanTargetToChooseANewPassword()
        {
            var target = BuildUser();
            var context = new ControllerContextBuilder(target);
            var request = BuildRequest(target.Id, target.AccessLevel);
            request.Password = "OperatorSelected9!";

            var result = await context.Controller.UpdateUser(request);

            Assert.IsType<OkObjectResult>(result);
            Assert.True(target.MustChangePassword);
        }

        [Fact]
        public async Task UpdateUser_OperatorPasswordResetDoesNotForceServiceAccountPasswordChanges()
        {
            var target = BuildUser();
            target.IsServiceAccount = true;
            var context = new ControllerContextBuilder(target);
            var request = BuildRequest(target.Id, target.AccessLevel);
            request.Password = "OperatorSelected9!";

            var result = await context.Controller.UpdateUser(request);

            Assert.IsType<OkObjectResult>(result);
            Assert.False(target.MustChangePassword);
        }

        [Fact]
        public async Task UpdateUser_SelfSelectedPasswordDoesNotSetOperatorChangeRequirement()
        {
            var target = BuildUser();
            var context = new ControllerContextBuilder(target, EnumAccessLevel.Employee, callerIsTarget: true);
            var request = BuildRequest(target.Id, target.AccessLevel);
            request.Password = "SelfSelected9!";

            var result = await context.Controller.UpdateUser(request);

            Assert.IsType<OkObjectResult>(result);
            Assert.False(target.MustChangePassword);
        }

        [Fact]
        public async Task DeleteUser_ManagerCannotDeleteAPeerWithoutIamManage()
        {
            var target = BuildUser();
            target.AccessLevel = (short)EnumAccessLevel.Manager;
            var context = new ControllerContextBuilder(target, EnumAccessLevel.Manager, iamDecision: PermissionDecision.Denied);

            var result = await context.Controller.DeleteUser(target.Id);

            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
            context.UserService.Verify(x => x.Delete(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUser_CallerCannotChangeTheirOwnRole()
        {
            var caller = BuildUser();
            var context = new ControllerContextBuilder(caller, EnumAccessLevel.Manager, callerIsTarget: true);

            var result = await context.Controller.UpdateUser(BuildRequest(caller.Id, (short)EnumAccessLevel.Employee));

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("self_role", badRequest.Value!.ToString());
            context.UserService.Verify(x => x.Update(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task UpdateUser_AdminChangingRole_RecordsAnIamAuditAndInvalidatesPermissions()
        {
            var target = BuildUser();
            var context = new ControllerContextBuilder(target);

            var result = await context.Controller.UpdateUser(BuildRequest(target.Id, (short)EnumAccessLevel.Manager));

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(1, target.IamVersion);
            context.Audit.Verify(writer => writer.Add(It.Is<AuditTrail>(entry =>
                entry.EntityName == AuditedEntities.Iam && entry.EntityId == target.Id && entry.Action == "EDIT")), Times.Once);
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
            public Mock<IUserService<User>> UserService { get; } = new();
            public Mock<IAuditTrailWriter> Audit { get; } = new();

            public ControllerContextBuilder(User stored, EnumAccessLevel callerLevel = EnumAccessLevel.Admin, bool callerIsTarget = false,
                PermissionDecision iamDecision = PermissionDecision.Allowed)
            {
                var caller = callerIsTarget ? stored : BuildUser();
                caller.AccessLevel = (short)callerLevel;
                var userRepository = new Mock<IRepository<User>>();
                userRepository.Setup(x => x.GetByIdAsync(stored.Id)).ReturnsAsync(stored);
                userRepository.Setup(x => x.GetByIdAsync(caller.Id)).ReturnsAsync(caller);

                UserService.Setup(x => x.Update(It.IsAny<User>())).Returns((User user) => user);

                var permissions = new Mock<IPermissionEvaluator>();
                permissions.Setup(x => x.DecideAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<User>(), It.IsAny<string>()))
                    .ReturnsAsync(iamDecision);
                permissions.Setup(x => x.GrantedAsync(It.IsAny<IReadOnlyCollection<User>>()))
                    .ReturnsAsync((IReadOnlyCollection<User> users) => (IReadOnlyDictionary<Guid, IReadOnlySet<string>>)users
                        .ToDictionary(user => user.Id, _ => (IReadOnlySet<string>)new HashSet<string>()));
                var mapper = new Mock<IMapper>();
                mapper.Setup(x => x.Map<UserDTO>(It.IsAny<User>())).Returns(new UserDTO());

                var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
                var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>().Object, configuration);
                var tokenService = new TokenService(new Mock<IRepository<RefreshToken>>().Object, configuration);

                var services = new ServiceCollection();
                services.AddSingleton(permissions.Object);
                services.AddSingleton(Audit.Object);
                var httpContext = new DefaultHttpContext
                {
                    RequestServices = services.BuildServiceProvider(),
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.GroupSid, EnterpriseId.ToString()),
                        new Claim(ClaimTypes.Sid, caller.Id.ToString())
                    ], "test"))
                };

                Controller = new UserController(
                    UserService.Object,
                    userRepository.Object,
                    mapper.Object,
                    configuration,
                    kmsProvider,
                    tokenService,
                    new Mock<IEmailService<EmailStructure>>().Object,
                    new RecaptchaVerifier(new HttpClient(), configuration, kmsProvider),
                    new PasswordLoginService(userRepository.Object),
                null!, null!)
                {
                    ControllerContext = new ControllerContext { HttpContext = httpContext }
                };
            }
        }
    }
}
