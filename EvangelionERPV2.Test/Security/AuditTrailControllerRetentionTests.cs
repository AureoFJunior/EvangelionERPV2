using AutoMapper;
using EvangelionERPV2.AuditModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    public class AuditTrailControllerRetentionTests
    {
        [Fact]
        public async Task CleanupRetention_ReturnsSummaryWithoutAuditPayload()
        {
            var enterpriseId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            var auditTrailService = new Mock<IAuditTrailService>(MockBehavior.Strict);
            auditTrailService
                .Setup(x => x.DeleteOlderThanAsync(enterpriseId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(7);

            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            userRepository
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new User
                {
                    Id = userId,
                    EnterpriseId = enterpriseId,
                    IsActive = true,
                    AccessLevel = (short)EnumAccessLevel.Admin
                });

            var mapper = new Mock<IMapper>(MockBehavior.Strict);
            var controller = new AuditTrailController(auditTrailService.Object, userRepository.Object, mapper.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
                            new Claim(ClaimTypes.Sid, userId.ToString())
                        ], "TestAuth"))
                    }
                }
            };

            var response = await controller.CleanupRetention(90);

            var ok = Assert.IsType<OkObjectResult>(response);
            var payload = ok.Value!;
            Assert.Equal(90, payload.GetType().GetProperty("RetentionDays")!.GetValue(payload));
            Assert.Equal(7, payload.GetType().GetProperty("DeletedCount")!.GetValue(payload));
            Assert.NotNull(payload.GetType().GetProperty("CutoffDateUtc"));
            Assert.Null(payload.GetType().GetProperty("AuditTrails"));
            Assert.Null(payload.GetType().GetProperty("ChangesJson"));
        }

        [Fact]
        public void CleanupRetention_RequiresAuditRetentionCleanupPolicy()
        {
            ControllerPolicyTestHelper.AssertActionPolicy<AuditTrailController>(
                nameof(AuditTrailController.CleanupRetention),
                "rbac:" + RbacPermissions.Audit.CleanupRetention);
        }
    }
}
