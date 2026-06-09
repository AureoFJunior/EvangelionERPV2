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
            emailService
                .Setup(service => service.TrySendQueuedEmail(It.IsAny<string>()))
                .ReturnsAsync(false);
            var controller = CreateController(emailService, withAdminClaims: true, setupQueuedEmailFallback: false);

            var result = await controller.SendEmail("Subject: test\r\n\r\nplain body without from/to");

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Email payload must include valid From and To headers.", badRequest.Value);
            emailService.Verify(service => service.TrySendQueuedEmail(It.IsAny<string>()), Times.Once);
            emailService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendEmail_WithValidMime_CallsServiceAndReturnsOk()
        {
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            emailService
                .Setup(service => service.TrySendQueuedEmail(It.IsAny<string>()))
                .ReturnsAsync(false);
            emailService
                .Setup(service => service.SendEmail(It.IsAny<MimeMessage>(), It.IsAny<Guid?>()))
                .Returns(Task.CompletedTask);

            var controller = CreateController(emailService, withAdminClaims: true, setupQueuedEmailFallback: false);
            var mime = "From: sender@example.com\r\nTo: receiver@example.com\r\nSubject: test\r\n\r\nhello";

            var result = await controller.SendEmail(mime);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Emails sent", ok.Value);
            emailService.Verify(service => service.TrySendQueuedEmail(It.IsAny<string>()), Times.Once);
            emailService.Verify(service => service.SendEmail(It.IsAny<MimeMessage>(), It.IsAny<Guid?>()), Times.Once);
            emailService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendEmail_WhenCcAndBccExceedRecipientLimit_ReturnsBadRequest()
        {
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            var controller = CreateController(emailService, withAdminClaims: true);
            var ccRecipients = string.Join(", ", Enumerable.Range(1, 25).Select(index => $"copy{index}@example.com"));
            var bccRecipients = string.Join(", ", Enumerable.Range(1, 25).Select(index => $"hidden{index}@example.com"));
            var mime = $"From: sender@example.com\r\nTo: receiver@example.com\r\nCc: {ccRecipients}\r\nBcc: {bccRecipients}\r\nSubject: test\r\n\r\nhello";

            var result = await controller.SendEmail(mime);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Recipient count must be 50 or lower.", badRequest.Value);
            emailService.Verify(service => service.TrySendQueuedEmail(It.IsAny<string>()), Times.Once);
            emailService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendQueuedEmail_WithValidSignedPayload_CallsServiceAndReturnsOk()
        {
            const string queuedPayload = "{\"enterpriseId\":\"00000000-0000-0000-0000-000000000001\"}";
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            emailService
                .Setup(service => service.TrySendQueuedEmail(queuedPayload))
                .ReturnsAsync(true);

            var controller = CreateController(emailService, withAdminClaims: true, setupQueuedEmailFallback: false);

            var result = await controller.SendQueuedEmail(queuedPayload);

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Queued email sent", ok.Value);
            emailService.Verify(service => service.TrySendQueuedEmail(queuedPayload), Times.Once);
            emailService.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendQueuedEmail_WithInvalidSignedPayload_ReturnsBadRequest()
        {
            const string queuedPayload = "not-a-signed-payload";
            var emailService = new Mock<IEmailService<EmailStructure>>(MockBehavior.Strict);
            emailService
                .Setup(service => service.TrySendQueuedEmail(queuedPayload))
                .ReturnsAsync(false);

            var controller = CreateController(emailService, withAdminClaims: true, setupQueuedEmailFallback: false);

            var result = await controller.SendQueuedEmail(queuedPayload);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Queued email payload must be a valid signed queued email message.", badRequest.Value);
            emailService.Verify(service => service.TrySendQueuedEmail(queuedPayload), Times.Once);
            emailService.VerifyNoOtherCalls();
        }

        private static EmailController CreateController(
            Mock<IEmailService<EmailStructure>> emailService,
            bool withAdminClaims = false,
            bool setupQueuedEmailFallback = true)
        {
            var mapper = new Mock<IMapper>(MockBehavior.Strict).Object;
            var userRepositoryMock = new Mock<IRepository<Shared.Entities.User>>(MockBehavior.Strict);
            if (setupQueuedEmailFallback)
            {
                emailService
                    .Setup(service => service.TrySendQueuedEmail(It.IsAny<string>()))
                    .ReturnsAsync(false);
            }

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
