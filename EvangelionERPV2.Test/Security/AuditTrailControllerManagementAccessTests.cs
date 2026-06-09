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
    public class AuditTrailControllerManagementAccessTests
    {
        private readonly Mock<IAuditTrailService> _auditTrailServiceMock = new();
        private readonly Mock<IRepository<User>> _userRepositoryMock = new();
        private readonly Mock<IMapper> _mapperMock = new();

        [Fact]
        public async Task GetAuditTrails_ReturnsUnauthorized_WhenEnterpriseClaimIsMissing()
        {
            var controller = CreateController(Array.Empty<Claim>());

            var response = await controller.GetAuditTrails();

            Assert.IsType<UnauthorizedResult>(response);
            _auditTrailServiceMock.Verify(
                x => x.GetAllAsyncFiltering(
                    It.IsAny<Guid>(),
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>(),
                    It.IsAny<Shared.DTOs.AuditTrailFilterDTO?>()),
                Times.Never);
        }

        [Fact]
        public void GetAuditTrails_RequiresAuditReadPolicy()
        {
            ControllerPolicyTestHelper.AssertActionPolicy<AuditTrailController>(
                nameof(AuditTrailController.GetAuditTrails),
                "rbac:" + RbacPermissions.Audit.Read);
        }

        [Fact]
        public async Task GetAuditTrails_ReturnsNoContent_WhenUserIsManagementAndNoData()
        {
            var userId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();

            _userRepositoryMock
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new User
                {
                    Id = userId,
                    EnterpriseId = enterpriseId,
                    IsActive = true,
                    AccessLevel = (short)EnumAccessLevel.Manager
                });

            _auditTrailServiceMock
                .Setup(x => x.GetAllAsyncFiltering(
                    enterpriseId,
                    true,
                    null,
                    null,
                    null))
                .ReturnsAsync((Enumerable.Empty<AuditTrail>(), 0));

            var controller = CreateController(new[]
            {
                new Claim(ClaimTypes.Sid, userId.ToString()),
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())
            });

            var response = await controller.GetAuditTrails();

            Assert.IsType<NoContentResult>(response);
            _auditTrailServiceMock.Verify(
                x => x.GetAllAsyncFiltering(enterpriseId, true, null, null, null),
                Times.Once);
        }

        private AuditTrailController CreateController(IEnumerable<Claim> claims)
        {
            var controller = new AuditTrailController(
                _auditTrailServiceMock.Object,
                _userRepositoryMock.Object,
                _mapperMock.Object);

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
    }
}
