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
    public class EmailControllerSendManualEmailValidationTests
    {
        [Fact]
        public async Task SendManualEmail_ReturnsBadRequest_WhenRecipientCountExceedsLimit()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            var userRepository = BuildAdminUserRepository(userId, enterpriseId);
            var controller = CreateController(emailService, userRepository, userId, enterpriseId);

            var recipients = Enumerable.Range(1, 51).Select(x => $"recipient{x}@example.com").ToArray();
            var payload = new EmailStructure("Body", "Subject", recipients);

            var response = await controller.SendManualEmail(payload);

            var badRequest = Assert.IsType<BadRequestObjectResult>(response);
            Assert.Equal("Recipient count must be 50 or lower.", badRequest.Value);
            emailService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendManualEmail_ReturnsBadRequest_WhenBodyExceedsLimit()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            var userRepository = BuildAdminUserRepository(userId, enterpriseId);
            var controller = CreateController(emailService, userRepository, userId, enterpriseId);

            var oversizedBody = new string('a', 20001);
            var payload = new EmailStructure(oversizedBody, "Subject", new[] { "dest@example.com" });

            var response = await controller.SendManualEmail(payload);

            var badRequest = Assert.IsType<BadRequestObjectResult>(response);
            Assert.Equal("Body must be 20000 characters or fewer.", badRequest.Value);
            emailService.VerifyNoOtherCalls();
        }

        private static Mock<IRepository<Shared.Entities.User>> BuildAdminUserRepository(Guid userId, Guid enterpriseId)
        {
            var repository = new Mock<IRepository<Shared.Entities.User>>(MockBehavior.Strict);
            repository
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new Shared.Entities.User
                {
                    Id = userId,
                    EnterpriseId = enterpriseId,
                    AccessLevel = (short)EnumAccessLevel.Admin,
                    IsActive = true,
                    UserName = "admin.user",
                    FirstName = "Admin",
                    LastName = "User",
                    Email = "admin@example.com",
                    BirthDate = DateTime.UtcNow.AddYears(-30)
                });

            return repository;
        }

        private static EmailController CreateController(
            Mock<IEmailService<EmailStructure>> emailService,
            Mock<IRepository<Shared.Entities.User>> userRepository,
            Guid userId,
            Guid enterpriseId)
        {
            var mapper = new Mock<IMapper>(MockBehavior.Strict).Object;
            var controller = new EmailController(emailService.Object, userRepository.Object, mapper);
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, userId.ToString()),
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())
            };

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"))
                }
            };

            return controller;
        }
    }
}
