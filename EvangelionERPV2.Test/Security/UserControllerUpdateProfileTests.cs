using Amazon.SecretsManager;
using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The self-service half of the profile screen: a person edits their own name and phone, and
    /// nobody else's.
    /// </summary>
    public class UserControllerUpdateProfileTests
    {
        [Fact]
        public async Task UpdateProfile_WithBlankName_ReturnsBadRequestAndTouchesNothing()
        {
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var controller = CreateController(userService.Object, userRepository.Object);
            SetAuthenticatedUser(controller, Guid.NewGuid(), Guid.NewGuid());

            var result = await controller.UpdateProfile(new UserController.UpdateProfileRequest
            {
                FirstName = "   ",
                LastName = "Ikari"
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("First name and last name are required.", badRequest.Value);
            userRepository.VerifyNoOtherCalls();
            userService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateProfile_WithOverlongPhone_ReturnsBadRequest()
        {
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var controller = CreateController(userService.Object, userRepository.Object);
            SetAuthenticatedUser(controller, Guid.NewGuid(), Guid.NewGuid());

            var result = await controller.UpdateProfile(new UserController.UpdateProfileRequest
            {
                FirstName = "Shinji",
                LastName = "Ikari",
                PhoneNumber = new string('9', 31)
            });

            Assert.IsType<BadRequestObjectResult>(result);
            userRepository.VerifyNoOtherCalls();
            userService.VerifyNoOtherCalls();
        }

        /// <remarks>
        /// The one that matters. The id comes from the token, so a caller cannot aim this at
        /// somebody else — but a user whose row belongs to another tenant must still be refused.
        /// </remarks>
        [Fact]
        public async Task UpdateProfile_ForAUserInAnotherEnterprise_IsForbidden()
        {
            var userId = Guid.NewGuid();
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            userRepository
                .Setup(repository => repository.GetByIdAsync(userId))
                .ReturnsAsync(new User { Id = userId, IsActive = true, EnterpriseId = Guid.NewGuid() });

            var controller = CreateController(userService.Object, userRepository.Object);
            SetAuthenticatedUser(controller, userId, Guid.NewGuid());

            var result = await controller.UpdateProfile(new UserController.UpdateProfileRequest
            {
                FirstName = "Shinji",
                LastName = "Ikari"
            });

            Assert.IsType<ForbidResult>(result);
            userService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateProfile_TrimsTheNameAndStoresThePhone()
        {
            var stored = Saved(out var userService, out var userRepository, out var controller, existingPhone: "");

            var result = await controller.UpdateProfile(new UserController.UpdateProfileRequest
            {
                FirstName = "  Shinji ",
                LastName = " Ikari  ",
                PhoneNumber = " 11 98888-7777 "
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Shinji", stored.FirstName);
            Assert.Equal("Ikari", stored.LastName);
            Assert.Equal("11 98888-7777", stored.PhoneNumber);
        }

        /// <remarks>
        /// A client that sends only the name must not wipe the number as a side effect — which is
        /// what a plain assignment from a null field would do.
        /// </remarks>
        [Fact]
        public async Task UpdateProfile_WithNoPhoneField_LeavesTheStoredPhoneAlone()
        {
            var stored = Saved(out _, out _, out var controller, existingPhone: "11 98888-7777");

            await controller.UpdateProfile(new UserController.UpdateProfileRequest
            {
                FirstName = "Shinji",
                LastName = "Ikari",
                PhoneNumber = null
            });

            Assert.Equal("11 98888-7777", stored.PhoneNumber);
        }

        [Fact]
        public async Task UpdateProfile_WithAnEmptyPhone_ClearsIt()
        {
            var stored = Saved(out _, out _, out var controller, existingPhone: "11 98888-7777");

            await controller.UpdateProfile(new UserController.UpdateProfileRequest
            {
                FirstName = "Shinji",
                LastName = "Ikari",
                PhoneNumber = ""
            });

            Assert.Equal(string.Empty, stored.PhoneNumber);
        }

        /// <summary>Wires a controller whose repository returns one editable user of this tenant.</summary>
        private static User Saved(
            out Mock<IUserService<User>> userService,
            out Mock<IRepository<User>> userRepository,
            out UserController controller,
            string existingPhone)
        {
            var userId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var user = new User
            {
                Id = userId,
                IsActive = true,
                EnterpriseId = enterpriseId,
                FirstName = "Old",
                LastName = "Name",
                PhoneNumber = existingPhone
            };

            userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            userService.Setup(service => service.Update(It.IsAny<User>())).Returns((User updated) => updated);

            userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            userRepository.Setup(repository => repository.GetByIdAsync(userId)).ReturnsAsync(user);

            controller = CreateController(userService.Object, userRepository.Object, MappingUser());
            SetAuthenticatedUser(controller, userId, enterpriseId);

            return user;
        }

        private static IMapper MappingUser()
        {
            var mapper = new Mock<IMapper>(MockBehavior.Strict);
            mapper
                .Setup(m => m.Map<UserDTO>(It.IsAny<User>()))
                .Returns((User user) => new UserDTO
                {
                    Id = user.Id,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    PhoneNumber = user.PhoneNumber
                });

            return mapper.Object;
        }

        private static UserController CreateController(
            IUserService<User> userService,
            IRepository<User> userRepository,
            IMapper? mapper = null)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object, configuration);
            var refreshTokenRepository = new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object;
            var tokenService = new TokenService(refreshTokenRepository, configuration);
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict).Object;

            return new UserController(
                userService,
                userRepository,
                mapper ?? new Mock<IMapper>(MockBehavior.Strict).Object,
                configuration,
                kmsProvider,
                tokenService,
                emailService,
                new RecaptchaVerifier(new HttpClient(), configuration, kmsProvider),
                new PasswordLoginService(userRepository));
        }

        private static void SetAuthenticatedUser(Controller controller, Guid userId, Guid enterpriseId)
        {
            var claimsPrincipal = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.Sid, userId.ToString()),
                        new Claim("uid", userId.ToString()),
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                        new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())
                    ],
                    "UnitTestAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = claimsPrincipal }
            };
        }
    }
}
