using Amazon.SecretsManager;
using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class UserControllerUpdateProfilePictureValidationTests
    {
        [Fact]
        public async Task UpdateProfilePicture_WithInvalidBase64_ReturnsBadRequest()
        {
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var controller = CreateController(userService.Object, userRepository.Object);
            SetAuthenticatedUser(controller, "unit-user");

            var result = await controller.UpdateProfilePicture(new UserController.UpdateProfilePictureRequest
            {
                ProfilePicture = "invalid$payload"
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("ProfilePicture must be a valid base64 payload.", badRequest.Value);
            userRepository.VerifyNoOtherCalls();
            userService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateProfilePicture_WithPayloadOver5Mb_ReturnsBadRequest()
        {
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var controller = CreateController(userService.Object, userRepository.Object);
            SetAuthenticatedUser(controller, "unit-user");

            var bytes = new byte[(5 * 1024 * 1024) + 1];
            var payload = Convert.ToBase64String(bytes);

            var result = await controller.UpdateProfilePicture(new UserController.UpdateProfilePictureRequest
            {
                ProfilePicture = payload
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("ProfilePicture must be 5 MB or smaller.", badRequest.Value);
            userRepository.VerifyNoOtherCalls();
            userService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task UpdateProfilePicture_WithUnsupportedImageType_ReturnsBadRequest()
        {
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var controller = CreateController(userService.Object, userRepository.Object);
            SetAuthenticatedUser(controller, "unit-user");

            var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not-an-image"));

            var result = await controller.UpdateProfilePicture(new UserController.UpdateProfilePictureRequest
            {
                ProfilePicture = payload
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("ProfilePicture must be PNG, JPEG, GIF, or WEBP.", badRequest.Value);
            userRepository.VerifyNoOtherCalls();
            userService.VerifyNoOtherCalls();
        }

        private static UserController CreateController(IUserService<User> userService, IRepository<User> userRepository)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var mapper = new Mock<IMapper>(MockBehavior.Strict).Object;
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object, configuration);
            var refreshTokenRepository = new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object;
            var tokenService = new TokenService(refreshTokenRepository, configuration);
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict).Object;

            return new UserController(
                userService,
                userRepository,
                mapper,
                configuration,
                kmsProvider,
                tokenService,
                emailService);
        }

        private static void SetAuthenticatedUser(Controller controller, string userName)
        {
            var claimsPrincipal = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userName)],
                    "UnitTestAuth"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = claimsPrincipal
                }
            };
        }
    }
}
