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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The user table is the one place where a tenant leak hands over login names and access
    /// levels, so every action here compares the row's enterprise after the fetch. These cover the
    /// routes that had no test at all: read one, create, delete, and the two self-service settings.
    /// </summary>
    public class UserControllerTenantScopedActionsTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Mock<IUserService<User>> _service = new();
        private readonly Mock<IRepository<User>> _repository = new();

        private UserController Controller() => Build().WithTenant(_enterpriseId, _userId);

        private UserController Anonymous() => Build().WithoutTenant();

        private UserController TenantWithoutUserClaim() => Build().WithTenant(_enterpriseId);

        private UserController Build()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>().Object, configuration);
            var tokenService = new TokenService(new Mock<IRepository<RefreshToken>>().Object, configuration);

            return new UserController(
                _service.Object,
                _repository.Object,
                Mapper(),
                configuration,
                kmsProvider,
                tokenService,
                new Mock<IEmailService<EmailStructure>>().Object,
                new RecaptchaVerifier(new HttpClient(), configuration, kmsProvider),
                new PasswordLoginService(_repository.Object),
                null!, null!);
        }

        private static IMapper Mapper()
        {
            var mapper = new Mock<IMapper>();
            mapper.Setup(m => m.Map<UserDTO>(It.IsAny<User>()))
                .Returns<User>(u => new UserDTO
                {
                    Id = u.Id,
                    FirstName = u.FirstName,
                    Email = u.Email,
                    AccessLevel = u.AccessLevel,
                    ActualTheme = u.ActualTheme,
                    Language = u.Language,
                    ProfilePicture = u.ProfilePicture ?? string.Empty
                });
            return mapper.Object;
        }

        private User NewUser(Guid? enterpriseId = null, bool isActive = true, string? picture = null) => new()
        {
            Id = _userId,
            FirstName = "Aureo",
            LastName = "Junior",
            UserName = "aureo",
            Email = "aureo@example.com",
            EnterpriseId = enterpriseId ?? _enterpriseId,
            IsActive = isActive,
            AccessLevel = (short)EnumAccessLevel.Employee,
            ProfilePicture = picture ?? string.Empty
        };

        private static CreateUserRequestDTO NewUserRequest() => new()
        {
            FirstName = "  Aureo  ",
            LastName = "  Junior  ",
            UserName = "  aureo  ",
            Email = "  aureo@example.com  ",
            Password = "s3nh4-forte",
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Language = (short)EnumLanguage.Portuguese
        };

        [Fact]
        public async Task GetUser_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetUser(Guid.NewGuid()));
        }

        [Fact]
        public async Task GetUser_FromAnotherTenant_AnswersNoContentRatherThanTheRow()
        {
            var foreign = NewUser(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            Assert.IsType<NoContentResult>(await Controller().GetUser(foreign.Id));
        }

        [Fact]
        public async Task GetUser_ThatWasDeactivated_AnswersNoContent()
        {
            var inactive = NewUser(isActive: false);
            _repository.Setup(r => r.GetByIdAsync(inactive.Id)).ReturnsAsync(inactive);

            Assert.IsType<NoContentResult>(await Controller().GetUser(inactive.Id));
        }

        [Fact]
        public async Task GetUser_OwnedByTheCallersTenant_AnswersTheRow()
        {
            var user = NewUser();
            _repository.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

            var result = Assert.IsType<OkObjectResult>(await Controller().GetUser(user.Id));
            Assert.Equal("aureo@example.com", Assert.IsType<UserDTO>(result.Value).Email);
        }

        /// <summary>The stored value is a path, so a null one comes back as empty and costs no fetch.</summary>
        [Fact]
        public async Task GetUser_WithNoProfilePicture_CostsNoPictureFetch()
        {
            var user = NewUser();
            _repository.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);

            await Controller().GetUser(user.Id);

            _service.Verify(s => s.GetProfilePictureBase64Async(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetUser_WithAProfilePicture_ResolvesTheStoredPath()
        {
            var user = NewUser(picture: "users/aureo.jpg");
            _repository.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
            _service.Setup(s => s.GetProfilePictureBase64Async("users/aureo.jpg")).ReturnsAsync("base64");

            var result = Assert.IsType<OkObjectResult>(await Controller().GetUser(user.Id));

            Assert.Equal("base64", Assert.IsType<UserDTO>(result.Value).ProfilePicture);
        }

        [Fact]
        public async Task AddUser_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().AddUser(NewUserRequest()));
        }

        /// <summary>
        /// A created user lands in the caller's tenant at the **Employee** tier whatever the body
        /// says — there is no access level on the request, and a create endpoint that accepted one
        /// would be a privilege-escalation route.
        /// </summary>
        [Fact]
        public async Task AddUser_AlwaysCreatesAnEmployeeInTheCallersTenant()
        {
            User? created = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<User>()))
                .Callback<User>(u => created = u)
                .ReturnsAsync((User u) => u);

            Assert.IsType<OkObjectResult>(await Controller().AddUser(NewUserRequest()));

            Assert.Equal(_enterpriseId, created!.EnterpriseId);
            Assert.Equal((short)EnumAccessLevel.Employee, created.AccessLevel);
            Assert.True(created.IsActive);
            Assert.Equal((short)0, created.IsLogged);
        }

        [Fact]
        public async Task AddUser_TrimsTheFieldsItStores()
        {
            User? created = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<User>()))
                .Callback<User>(u => created = u)
                .ReturnsAsync((User u) => u);

            await Controller().AddUser(NewUserRequest());

            Assert.Equal("Aureo", created!.FirstName);
            Assert.Equal("Junior", created.LastName);
            Assert.Equal("aureo", created.UserName);
            Assert.Equal("aureo@example.com", created.Email);
        }

        /// <summary>
        /// The password is stored as it arrived — hashing belongs to the service, and trimming it
        /// here would silently change somebody's credential.
        /// </summary>
        [Fact]
        public async Task AddUser_DoesNotTrimThePassword()
        {
            User? created = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<User>()))
                .Callback<User>(u => created = u)
                .ReturnsAsync((User u) => u);

            var request = NewUserRequest();
            request.Password = "  s3nh4 com espaco  ";

            await Controller().AddUser(request);

            Assert.Equal("  s3nh4 com espaco  ", created!.Password);
        }

        /// <summary>
        /// A duplicate e-mail is the user's mistake and says so; every other `ArgumentException`
        /// keeps travelling, because swallowing them would hide a real fault as a validation error.
        /// </summary>
        [Fact]
        public async Task AddUser_WithAnEmailAlreadyInUse_AnswersBadRequest()
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<User>()))
                .ThrowsAsync(new ArgumentException("Email is already in use."));

            var result = Assert.IsType<BadRequestObjectResult>(await Controller().AddUser(NewUserRequest()));

            Assert.Equal("Email is already in use.", result.Value);
        }

        [Fact]
        public async Task AddUser_WhenTheServiceFailsForAnyOtherReason_DoesNotSwallowIt()
        {
            _service.Setup(s => s.CreateAsync(It.IsAny<User>()))
                .ThrowsAsync(new ArgumentException("Password does not meet the policy."));

            await Assert.ThrowsAsync<ArgumentException>(() => Controller().AddUser(NewUserRequest()));
        }

        [Fact]
        public async Task DeleteUser_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().DeleteUser(Guid.NewGuid()));
        }

        [Fact]
        public async Task DeleteUser_FromAnotherTenant_AnswersNoContentAndDeletesNothing()
        {
            var foreign = NewUser(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            Assert.IsType<NoContentResult>(await Controller().DeleteUser(foreign.Id));
            _service.Verify(s => s.Delete(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task DeleteUser_ThatIsAlreadyInactive_AnswersNoContent()
        {
            var inactive = NewUser(isActive: false);
            _repository.Setup(r => r.GetByIdAsync(inactive.Id)).ReturnsAsync(inactive);

            Assert.IsType<NoContentResult>(await Controller().DeleteUser(inactive.Id));
            _service.Verify(s => s.Delete(It.IsAny<Guid>()), Times.Never);
        }

        /// <summary>
        /// The tenant is checked twice on purpose: once on the row that was read, and again on what
        /// the service handed back, so a service that resolved a different row cannot leak it.
        /// </summary>
        [Fact]
        public async Task DeleteUser_WhenTheServiceReturnsAnotherTenantsRow_AnswersNoContent()
        {
            var user = NewUser();
            _repository.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
            _service.Setup(s => s.Delete(user.Id)).Returns(NewUser(Guid.NewGuid()));

            Assert.IsType<NoContentResult>(await Controller().DeleteUser(user.Id));
        }

        [Fact]
        public async Task DeleteUser_OwnedByTheCallersTenant_DeactivatesIt()
        {
            var user = NewUser();
            _repository.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
            _service.Setup(s => s.Delete(user.Id)).Returns(user);

            Assert.IsType<OkObjectResult>(await Controller().DeleteUser(user.Id));
        }

        /// <summary>The access-level list is a fixed enum, not a tenant's data.</summary>
        [Fact]
        public async Task GetAccessLevel_ListsEveryTierOnce()
        {
            var result = Assert.IsType<OkObjectResult>(await Controller().GetAccessLevel());
            var levels = Assert.IsAssignableFrom<IEnumerable<object>>(result.Value);

            Assert.Equal(Enum.GetValues<EnumAccessLevel>().Length, levels.Count());
        }

        [Fact]
        public async Task UpdateTheme_WithNoUserClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await TenantWithoutUserClaim().UpdateTheme(new UserController.UpdateThemeRequest { Theme = 1 }));
        }

        [Fact]
        public async Task UpdateTheme_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().UpdateTheme(new UserController.UpdateThemeRequest { Theme = 1 }));
        }

        [Fact]
        public async Task UpdateTheme_WhenTheAccountIsGone_IsNotFound()
        {
            _repository.Setup(r => r.GetByIdAsync(_userId)).ReturnsAsync((User)null!);

            Assert.IsType<NotFoundResult>(
                await Controller().UpdateTheme(new UserController.UpdateThemeRequest { Theme = 1 }));
        }

        /// <summary>
        /// A token whose tenant no longer matches the stored row is forbidden rather than
        /// not-found: the caller is real and the row is theirs, but the token is stale.
        /// </summary>
        [Fact]
        public async Task UpdateTheme_WhenTheTokensTenantNoLongerMatchesTheRow_IsForbidden()
        {
            _repository.Setup(r => r.GetByIdAsync(_userId)).ReturnsAsync(NewUser(Guid.NewGuid()));

            Assert.IsType<ForbidResult>(
                await Controller().UpdateTheme(new UserController.UpdateThemeRequest { Theme = 1 }));
        }

        [Fact]
        public async Task UpdateTheme_WritesTheThemeOntoTheCallersOwnRow()
        {
            var user = NewUser();
            _repository.Setup(r => r.GetByIdAsync(_userId)).ReturnsAsync(user);
            _service.Setup(s => s.Update(It.IsAny<User>())).Returns<User>(u => u);

            Assert.IsType<OkObjectResult>(
                await Controller().UpdateTheme(new UserController.UpdateThemeRequest { Theme = 4 }));

            Assert.Equal((short)4, user.ActualTheme);
        }

        [Fact]
        public async Task UpdateLanguage_WithALanguageOutsideTheEnum_IsRefused()
        {
            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().UpdateLanguage(new UserController.UpdateLanguageRequest { Language = 99 }));

            Assert.Equal("Invalid language value.", result.Value);
        }

        [Fact]
        public async Task UpdateLanguage_WithNoUserClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await TenantWithoutUserClaim().UpdateLanguage(
                new UserController.UpdateLanguageRequest { Language = (int)EnumLanguage.Portuguese }));
        }

        [Fact]
        public async Task UpdateLanguage_WhenTheAccountIsGone_IsNotFound()
        {
            _repository.Setup(r => r.GetByIdAsync(_userId)).ReturnsAsync((User)null!);

            Assert.IsType<NotFoundResult>(await Controller().UpdateLanguage(
                new UserController.UpdateLanguageRequest { Language = (int)EnumLanguage.Portuguese }));
        }

        [Fact]
        public async Task UpdateLanguage_WhenTheTokensTenantNoLongerMatchesTheRow_IsForbidden()
        {
            _repository.Setup(r => r.GetByIdAsync(_userId)).ReturnsAsync(NewUser(Guid.NewGuid()));

            Assert.IsType<ForbidResult>(await Controller().UpdateLanguage(
                new UserController.UpdateLanguageRequest { Language = (int)EnumLanguage.Portuguese }));
        }

        [Fact]
        public async Task UpdateLanguage_WritesTheLanguageOntoTheCallersOwnRow()
        {
            var user = NewUser();
            _repository.Setup(r => r.GetByIdAsync(_userId)).ReturnsAsync(user);
            _service.Setup(s => s.Update(It.IsAny<User>())).Returns<User>(u => u);

            Assert.IsType<OkObjectResult>(await Controller().UpdateLanguage(
                new UserController.UpdateLanguageRequest { Language = (int)EnumLanguage.Japanese }));

            Assert.Equal((short)EnumLanguage.Japanese, user.Language);
        }

        [Fact]
        public async Task UpdateLanguage_WhenTheServiceRefusesTheWrite_AnswersNoContent()
        {
            _repository.Setup(r => r.GetByIdAsync(_userId)).ReturnsAsync(NewUser());
            _service.Setup(s => s.Update(It.IsAny<User>())).Returns((User)null!);

            Assert.IsType<NoContentResult>(await Controller().UpdateLanguage(
                new UserController.UpdateLanguageRequest { Language = (int)EnumLanguage.Portuguese }));
        }
    }
}
