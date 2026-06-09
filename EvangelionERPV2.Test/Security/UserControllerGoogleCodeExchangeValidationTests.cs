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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    public class UserControllerGoogleCodeExchangeValidationTests
    {
        [Fact]
        public async Task LoginWithGoogleCode_WhenCodeVerifierIsMissing_ReturnsBadRequest()
        {
            var (controller, _, _, secretManager) = CreateController();

            var result = await controller.LoginWithGoogleCode(new GoogleCodeExchangeRequest
            {
                Code = "auth-code-123",
                RedirectUri = "https://app.evangelionerp.com/auth/google/callback",
                CodeVerifier = string.Empty
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("codeVerifier is required and must be a valid PKCE value.", badRequest.Value);
            secretManager.Verify(
                x => x.GetSecretValueAsync(It.IsAny<Amazon.SecretsManager.Model.GetSecretValueRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task LoginWithGoogleCode_WhenRedirectUriOriginIsNotAllowed_ReturnsBadRequest()
        {
            var (controller, _, _, secretManager) = CreateController();

            var result = await controller.LoginWithGoogleCode(new GoogleCodeExchangeRequest
            {
                Code = "auth-code-123",
                RedirectUri = "https://evil.example.com/callback",
                CodeVerifier = new string('a', 43)
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("redirectUri is not allowed.", badRequest.Value);
            secretManager.Verify(
                x => x.GetSecretValueAsync(It.IsAny<Amazon.SecretsManager.Model.GetSecretValueRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static (
            UserController controller,
            Mock<IUserService<User>> userService,
            Mock<IRepository<User>> userRepository,
            Mock<IAmazonSecretsManager> secretManager) CreateController()
        {
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = new Mock<IMapper>(MockBehavior.Strict);
            var secretManager = new Mock<IAmazonSecretsManager>(MockBehavior.Strict);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://app.evangelionerp.com"
            }).Build();

            var kmsProvider = new AWSKMSKeyProvider(secretManager.Object, configuration);
            var tokenService = new TokenService(new Mock<IRepository<RefreshToken>>(MockBehavior.Strict).Object, configuration);
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);

            var controller = new UserController(
                userService.Object,
                userRepository.Object,
                mapper.Object,
                configuration,
                kmsProvider,
                tokenService,
                emailService.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            return (controller, userService, userRepository, secretManager);
        }
    }
}
