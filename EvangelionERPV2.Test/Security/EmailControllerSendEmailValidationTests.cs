using AutoMapper;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class EmailControllerSendEmailValidationTests
    {
        [Fact]
        public async Task SendEmail_WithEmptyPayload_ReturnsBadRequest()
        {
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            var controller = CreateController(emailService);

            var result = await controller.SendEmail(string.Empty);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Email payload is required.", badRequest.Value);
            emailService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendEmail_WithMissingHeaders_ReturnsBadRequest()
        {
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            var controller = CreateController(emailService, withAdminClaims: true);

            var result = await controller.SendEmail("Subject: test\r\n\r\nplain body without from/to");

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Email payload must include valid From and To headers.", badRequest.Value);
            emailService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendEmail_WithValidMime_CallsServiceAndReturnsOk()
        {
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            emailService
                .Setup(service => service.SendEmail(It.IsAny<MimeMessage>()))
                .Returns(Task.CompletedTask);

            var controller = CreateController(emailService, withAdminClaims: true);
            var mime = "From: sender@example.com\r\nTo: receiver@example.com\r\nSubject: test\r\n\r\nhello";

            var result = await controller.SendEmail(mime);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Emails sent", ok.Value);
            emailService.Verify(service => service.SendEmail(It.IsAny<MimeMessage>()), Times.Once);
        }

        private static EmailController CreateController(
            Mock<IEmailService<EmailStructure>> emailService,
            bool withAdminClaims = false)
        {
            var mapper = new Mock<IMapper>(MockBehavior.Strict).Object;
            var userRepositoryMock = new Mock<IRepository<Shared.Entities.User>>(MockBehavior.Strict);
            var controller = new EmailController(emailService.Object, userRepositoryMock.Object, mapper);

            if (!withAdminClaims)
                return controller;

            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            userRepositoryMock
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new Shared.Entities.User
                {
                    Id = userId,
                    EnterpriseId = enterpriseId,
                    AccessLevel = (short)EnumAccessLevel.Admin,
                    IsActive = true
                });

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                        new Claim(ClaimTypes.Sid, userId.ToString())
                    ], "TestAuth"))
                }
            };

            return controller;
        }
    }
}
