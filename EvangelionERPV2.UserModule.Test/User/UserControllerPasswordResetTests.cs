using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Token;
using Microsoft.AspNetCore.Http;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MimeKit;
using Moq;
using Xunit;

namespace EvangelionERPV2.UserModule.Test.User
{
    public class UserControllerPasswordResetTests
    {
        [Fact]
        public async Task RequestPasswordReset_WhenUserExists_ReturnsOkAndSendsEmail()
        {
            var (controller, userService, emailService) = CreateController();
            var enterpriseId = Guid.NewGuid();
            userService.Setup(s => s.CreatePasswordResetTokenContextAsync("user@evangelion.com"))
                .ReturnsAsync(("token-123", enterpriseId));
            emailService.Setup(s => s.CreateEmail(It.IsAny<EmailStructure>(), enterpriseId))
                .ReturnsAsync(new MimeMessage());
            emailService.Setup(s => s.SendEmail(It.IsAny<MimeMessage>(), enterpriseId))
                .Returns(Task.CompletedTask);

            var result = await controller.RequestPasswordReset(new UserController.RequestPasswordResetRequest
            {
                Email = "user@evangelion.com"
            });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, ok.StatusCode ?? 200);
            emailService.Verify(s => s.SendEmail(It.IsAny<MimeMessage>(), enterpriseId), Times.Once);
        }

        [Fact]
        public async Task RequestPasswordReset_WhenUserMissing_ReturnsOkAndDoesNotSendEmail()
        {
            var (controller, userService, emailService) = CreateController();
            userService.Setup(s => s.CreatePasswordResetTokenContextAsync(It.IsAny<string>()))
                .ReturnsAsync(((string?)null, (Guid?)null));

            var result = await controller.RequestPasswordReset(new UserController.RequestPasswordResetRequest
            {
                Email = "missing@evangelion.com"
            });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, ok.StatusCode ?? 200);
            emailService.Verify(s => s.SendEmail(It.IsAny<MimeMessage>(), It.IsAny<Guid?>()), Times.Never);
        }

        [Fact]
        public async Task RequestPasswordReset_WhenEmailIsEmpty_ReturnsOkAndDoesNotCallService()
        {
            var (controller, userService, emailService) = CreateController();

            var result = await controller.RequestPasswordReset(new UserController.RequestPasswordResetRequest
            {
                Email = "   "
            });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, ok.StatusCode ?? 200);
            userService.Verify(s => s.CreatePasswordResetTokenContextAsync(It.IsAny<string>()), Times.Never);
            emailService.Verify(s => s.SendEmail(It.IsAny<MimeMessage>(), It.IsAny<Guid?>()), Times.Never);
        }

        [Fact]
        public async Task ResetPassword_WhenServiceThrowsArgumentException_ReturnsBadRequest()
        {
            var (controller, userService, _) = CreateController();
            userService.Setup(s => s.ResetPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new ArgumentException("invalid"));

            var result = await controller.ResetPassword(new UserController.ResetPasswordRequest
            {
                Email = "user@evangelion.com",
                Token = "invalid",
                NewPassword = "newpassword123"
            });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ResetPassword_WhenValid_ReturnsOk()
        {
            var (controller, userService, _) = CreateController();
            userService.Setup(s => s.ResetPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

            var result = await controller.ResetPassword(new UserController.ResetPasswordRequest
            {
                Email = "user@evangelion.com",
                Token = "valid-token",
                NewPassword = "newpassword123"
            });

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, ok.StatusCode ?? 200);
        }

        [Fact]
        public async Task ResetPassword_WhenTooManyFailuresForSameIpAndEmail_ReturnsTooManyRequests()
        {
            var (controller, userService, _) = CreateController();
            var email = $"limit-{Guid.NewGuid():N}@evangelion.com";

            userService.Setup(s => s.ResetPasswordAsync(email, It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new ArgumentException("invalid"));

            for (var i = 0; i < 5; i++)
            {
                var badRequest = await controller.ResetPassword(new UserController.ResetPasswordRequest
                {
                    Email = email,
                    Token = "00000000",
                    NewPassword = "Password1!"
                });

                Assert.IsType<BadRequestObjectResult>(badRequest);
            }

            var throttled = await controller.ResetPassword(new UserController.ResetPasswordRequest
            {
                Email = email,
                Token = "00000000",
                NewPassword = "Password1!"
            });

            var tooMany = Assert.IsType<ObjectResult>(throttled);
            Assert.Equal(StatusCodes.Status429TooManyRequests, tooMany.StatusCode);
        }

        [Fact]
        public async Task ResetPassword_WhenSuccessAfterFailures_ClearsIpAndEmailLimitBucket()
        {
            var (controller, userService, _) = CreateController();
            var email = $"clear-{Guid.NewGuid():N}@evangelion.com";
            var callCount = 0;

            userService.Setup(s => s.ResetPasswordAsync(email, It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string emailArg, string tokenArg, string newPasswordArg) =>
                {
                    callCount++;
                    if (callCount == 5)
                        return Task.CompletedTask;

                    return Task.FromException(new ArgumentException("invalid"));
                });

            for (var i = 0; i < 4; i++)
            {
                var badRequest = await controller.ResetPassword(new UserController.ResetPasswordRequest
                {
                    Email = email,
                    Token = "00000000",
                    NewPassword = "Password1!"
                });

                Assert.IsType<BadRequestObjectResult>(badRequest);
            }

            var success = await controller.ResetPassword(new UserController.ResetPasswordRequest
            {
                Email = email,
                Token = "00000000",
                NewPassword = "Password1!"
            });
            Assert.IsType<OkObjectResult>(success);

            var afterSuccessFailure1 = await controller.ResetPassword(new UserController.ResetPasswordRequest
            {
                Email = email,
                Token = "00000000",
                NewPassword = "Password1!"
            });
            Assert.IsType<BadRequestObjectResult>(afterSuccessFailure1);

            var afterSuccessFailure2 = await controller.ResetPassword(new UserController.ResetPasswordRequest
            {
                Email = email,
                Token = "00000000",
                NewPassword = "Password1!"
            });
            Assert.IsType<BadRequestObjectResult>(afterSuccessFailure2);

            userService.Verify(s => s.ResetPasswordAsync(email, It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(7));
        }

        private static (
            UserController controller,
            Mock<IUserService<Shared.Entities.User>> userService,
            Mock<IEmailService<EmailStructure>> emailService) CreateController()
        {
            var userService = new Mock<IUserService<Shared.Entities.User>>();
            var userRepository = new Mock<IRepository<Shared.Entities.User>>();
            var mapper = new Mock<IMapper>();
            var emailService = new Mock<IEmailService<EmailStructure>>();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AWSSettings:SecretName"] = "test-secret",
                    ["Frontend:BaseUrl"] = "https://app.evangelionerp.com"
                })
                .Build();

            var secretsManager = new Mock<IAmazonSecretsManager>();
            secretsManager.Setup(s => s.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse
                {
                    SecretString = "{\"access-key-id\":\"test\",\"secret-access-key\":\"test\"}"
                });
            var kmsProvider = new AWSKMSKeyProvider(secretsManager.Object, configuration);

            var refreshTokenRepository = new Mock<IRepository<RefreshToken>>();
            var tokenService = new TokenService(refreshTokenRepository.Object, configuration);

            var controller = new UserController(
                userService.Object,
                userRepository.Object,
                mapper.Object,
                configuration,
                kmsProvider,
                tokenService,
                emailService.Object);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
            controller.ControllerContext.HttpContext.Request.Headers["X-Real-IP"] = $"test-{Guid.NewGuid():N}";

            return (controller, userService, emailService);
        }
    }
}
