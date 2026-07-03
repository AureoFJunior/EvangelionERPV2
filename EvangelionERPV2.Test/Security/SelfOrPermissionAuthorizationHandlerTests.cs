using System.Security.Claims;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    public class SelfOrPermissionAuthorizationHandlerTests
    {
        [Fact]
        public async Task HandleRequirementAsync_AllowsAdminForDifferentTargetUser()
        {
            var userId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();

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

            var handler = new SelfOrPermissionAuthorizationHandler(userRepository.Object);
            var requirement = new SelfOrPermissionRequirement(
                RbacPermissions.Users.ReadSelf,
                RbacPermissions.Users.Read,
                "id");

            var user = BuildPrincipal(userId, enterpriseId);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.RouteValues["id"] = targetUserId.ToString();

            var context = new AuthorizationHandlerContext([requirement], user, httpContext);
            await handler.HandleAsync(context);

            Assert.True(context.HasSucceeded);
        }

        [Fact]
        public async Task HandleRequirementAsync_AllowsEmployeeOnlyForOwnRouteId()
        {
            var userId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();

            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            userRepository
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new User
                {
                    Id = userId,
                    EnterpriseId = enterpriseId,
                    IsActive = true,
                    AccessLevel = (short)EnumAccessLevel.Employee
                });

            var handler = new SelfOrPermissionAuthorizationHandler(userRepository.Object);
            var requirement = new SelfOrPermissionRequirement(
                RbacPermissions.Users.ReadSelf,
                RbacPermissions.Users.Read,
                "id");

            var user = BuildPrincipal(userId, enterpriseId);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.RouteValues["id"] = userId.ToString();

            var context = new AuthorizationHandlerContext([requirement], user, httpContext);
            await handler.HandleAsync(context);

            Assert.True(context.HasSucceeded);
        }

        [Fact]
        public async Task HandleRequirementAsync_DeniesEmployeeForDifferentRouteId()
        {
            var userId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var targetUserId = Guid.NewGuid();

            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            userRepository
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new User
                {
                    Id = userId,
                    EnterpriseId = enterpriseId,
                    IsActive = true,
                    AccessLevel = (short)EnumAccessLevel.Employee
                });

            var handler = new SelfOrPermissionAuthorizationHandler(userRepository.Object);
            var requirement = new SelfOrPermissionRequirement(
                RbacPermissions.Users.ReadSelf,
                RbacPermissions.Users.Read,
                "id");

            var user = BuildPrincipal(userId, enterpriseId);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.RouteValues["id"] = targetUserId.ToString();

            var context = new AuthorizationHandlerContext([requirement], user, httpContext);
            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
        }

        private static ClaimsPrincipal BuildPrincipal(Guid userId, Guid enterpriseId)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Sid, userId.ToString()),
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())
            ], "TestAuth"));
        }
    }
}
