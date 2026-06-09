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
using MimeKit;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    public class UserControllerPasswordResetTenantScopeTests
    {
        [Fact]
        public async Task RequestPasswordReset_WhenTokenIsIssued_SendsEmailUsingTenantScopedSettings()
        {
            var (controller, userService, _, emailService) = CreateController();
            var enterpriseId = Guid.NewGuid();
            const string email = "user@evangelion.com";

            userService
                .Setup(x => x.CreatePasswordResetTokenContextAsync(email))
                .ReturnsAsync(("12345678", enterpriseId));

            emailService
                .Setup(x => x.CreateEmail(It.IsAny<EmailStructure>(), enterpriseId))
                .ReturnsAsync(new MimeMessage());

            emailService
                .Setup(x => x.SendEmail(It.IsAny<MimeMessage>(), enterpriseId))
                .Returns(Task.CompletedTask);

            var result = await controller.RequestPasswordReset(new UserController.RequestPasswordResetRequest
            {
                Email = email
            });

            Assert.IsType<OkObjectResult>(result);
            emailService.Verify(x => x.CreateEmail(It.IsAny<EmailStructure>(), enterpriseId), Times.Once);
            emailService.Verify(x => x.SendEmail(It.IsAny<MimeMessage>(), enterpriseId), Times.Once);
        }

        [Fact]
        public async Task RequestPasswordReset_WhenTenantScopeIsMissing_DoesNotSendEmail()
        {
            var (controller, userService, _, emailService) = CreateController();

            userService
                .Setup(x => x.CreatePasswordResetTokenContextAsync("user@evangelion.com"))
                .ReturnsAsync(("12345678", (Guid?)null));

            var result = await controller.RequestPasswordReset(new UserController.RequestPasswordResetRequest
            {
                Email = "user@evangelion.com"
            });

            Assert.IsType<OkObjectResult>(result);
            emailService.Verify(x => x.CreateEmail(It.IsAny<EmailStructure>(), It.IsAny<Guid?>()), Times.Never);
            emailService.Verify(x => x.SendEmail(It.IsAny<MimeMessage>(), It.IsAny<Guid?>()), Times.Never);
        }

        private static (
            UserController controller,
            Mock<IUserService<User>> userService,
            Mock<IRepository<User>> userRepository,
            Mock<IEmailService<EmailStructure>> emailService) CreateController()
        {
            var userService = new Mock<IUserService<User>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var mapper = new Mock<IMapper>(MockBehavior.Strict);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://app.evangelionerp.com/reset"
            }).Build();
            var kmsProvider = new AWSKMSKeyProvider(new Mock<IAmazonSecretsManager>(MockBehavior.Strict).Object, configuration);
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

            return (controller, userService, userRepository, emailService);
        }
    }
}
