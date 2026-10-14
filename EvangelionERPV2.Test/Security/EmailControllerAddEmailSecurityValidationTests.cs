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
    public class EmailControllerAddEmailSecurityValidationTests
    {
        [Fact]
        public async Task AddEmail_WithLoopbackHost_ReturnsBadRequest()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            var userRepository = BuildAdminUserRepository(userId, enterpriseId);
            var controller = CreateController(emailService, userRepository, userId, enterpriseId);

            var payload = new Email
            {
                HostName = "127.0.0.1",
                UserName = "sender@example.com",
                Password = "password",
                Port = 587
            };

            var response = await controller.AddEmail(payload);

            var badRequest = Assert.IsType<BadRequestObjectResult>(response);
            Assert.Equal("Invalid email settings payload.", badRequest.Value);
            emailService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task AddEmail_WithNonStandardSmtpPort_ReturnsBadRequest()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            var userRepository = BuildAdminUserRepository(userId, enterpriseId);
            var controller = CreateController(emailService, userRepository, userId, enterpriseId);

            var payload = new Email
            {
                HostName = "smtp.gmail.com",
                UserName = "sender@example.com",
                Password = "password",
                Port = 2525
            };

            var response = await controller.AddEmail(payload);

            var badRequest = Assert.IsType<BadRequestObjectResult>(response);
            Assert.Equal("Invalid email settings payload.", badRequest.Value);
            emailService.VerifyNoOtherCalls();
        }

        private static Mock<IRepository<User>> BuildAdminUserRepository(Guid userId, Guid enterpriseId)
        {
            var repository = new Mock<IRepository<User>>(MockBehavior.Strict);
            repository
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new User
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
            Mock<IRepository<User>> userRepository,
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
