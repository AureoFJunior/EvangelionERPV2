using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class EmailControllerSendManualEmailAuthorizationTests
    {
        [Fact]
        public async Task SendManualEmail_ReturnsUnauthorized_WhenEnterpriseClaimIsMissing()
        {
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<Shared.Entities.User>>(MockBehavior.Strict);
            var controller = CreateController(emailService, userRepository, Array.Empty<Claim>());

            var response = await controller.SendManualEmail(new EmailStructure());

            Assert.IsType<UnauthorizedResult>(response);
            emailService.VerifyNoOtherCalls();
            userRepository.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendManualEmail_ReturnsForbid_WhenCallerIsNotAdmin()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            var userRepository = new Mock<IRepository<Shared.Entities.User>>(MockBehavior.Strict);
            userRepository
                .Setup(repository => repository.GetByIdAsync(userId))
                .ReturnsAsync(BuildUser(userId, enterpriseId, EnumAccessLevel.Manager));

            var controller = CreateController(emailService, userRepository, new[]
            {
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                new Claim(ClaimTypes.Sid, userId.ToString())
            });

            var response = await controller.SendManualEmail(new EmailStructure());

            Assert.IsType<ForbidResult>(response);
            emailService.VerifyNoOtherCalls();
            userRepository.Verify(repository => repository.GetByIdAsync(userId), Times.Once);
        }

        [Fact]
        public async Task SendManualEmail_ReturnsOk_WhenCallerIsAdmin()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            emailService
                .Setup(service => service.SendManualEmail(
                    It.IsAny<EmailStructure>(),
                    It.Is<Enterprise>(enterprise => enterprise.Id == enterpriseId)))
                .Returns(Task.CompletedTask);

            var userRepository = new Mock<IRepository<Shared.Entities.User>>(MockBehavior.Strict);
            userRepository
                .Setup(repository => repository.GetByIdAsync(userId))
                .ReturnsAsync(BuildUser(userId, enterpriseId, EnumAccessLevel.Admin));

            var controller = CreateController(emailService, userRepository, new[]
            {
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                new Claim(ClaimTypes.Sid, userId.ToString())
            });

            var response = await controller.SendManualEmail(
                new EmailStructure("Body", "Subject", new[] { "dest@example.com" }));

            var ok = Assert.IsType<OkObjectResult>(response);
            Assert.Equal("Emails sended to the emails queue", ok.Value);
            emailService.Verify(
                service => service.SendManualEmail(
                    It.IsAny<EmailStructure>(),
                    It.Is<Enterprise>(enterprise => enterprise.Id == enterpriseId)),
                Times.Once);
            userRepository.Verify(repository => repository.GetByIdAsync(userId), Times.Once);
        }

        private static EmailController CreateController(
            Mock<IEmailService<EmailStructure>> emailService,
            Mock<IRepository<Shared.Entities.User>> userRepository,
            IEnumerable<Claim> claims)
        {
            var mapper = new Mock<IMapper>(MockBehavior.Strict).Object;
            var controller = new EmailController(emailService.Object, userRepository.Object, mapper);
            var identity = new ClaimsIdentity(claims, "TestAuthType");

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            };

            return controller;
        }

        private static Shared.Entities.User BuildUser(Guid userId, Guid enterpriseId, EnumAccessLevel accessLevel)
        {
            return new Shared.Entities.User
            {
                Id = userId,
                EnterpriseId = enterpriseId,
                IsActive = true,
                AccessLevel = (short)accessLevel,
                UserName = "user.test",
                FirstName = "User",
                LastName = "Test",
                Email = "user@evangelion.com",
                BirthDate = DateTime.UtcNow.AddYears(-25)
            };
        }
    }
}
