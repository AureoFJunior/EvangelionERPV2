using System.Security.Claims;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    public class PermissionAuthorizationHandlerTests
    {
        [Fact]
        public async Task HandleRequirementAsync_Denies_WhenUserIdClaimIsMissing()
        {
            var enterpriseId = Guid.NewGuid();
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var handler = new PermissionAuthorizationHandler(userRepository.Object);
            var requirement = new PermissionRequirement(RbacPermissions.Users.ReadSelf);
            var principal = BuildPrincipal(null, enterpriseId.ToString());
            var context = new AuthorizationHandlerContext([requirement], principal, null);

            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
            userRepository.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task HandleRequirementAsync_Denies_WhenUserIdClaimIsMalformed()
        {
            var enterpriseId = Guid.NewGuid();
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var handler = new PermissionAuthorizationHandler(userRepository.Object);
            var requirement = new PermissionRequirement(RbacPermissions.Users.ReadSelf);
            var principal = BuildPrincipal("invalid-guid", enterpriseId.ToString());
            var context = new AuthorizationHandlerContext([requirement], principal, null);

            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
            userRepository.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task HandleRequirementAsync_Denies_WhenEnterpriseClaimIsMissing()
        {
            var userId = Guid.NewGuid();
            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            var handler = new PermissionAuthorizationHandler(userRepository.Object);
            var requirement = new PermissionRequirement(RbacPermissions.Users.ReadSelf);
            var principal = BuildPrincipal(userId.ToString(), null);
            var context = new AuthorizationHandlerContext([requirement], principal, null);

            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
            userRepository.Verify(x => x.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task HandleRequirementAsync_Denies_WhenUserBelongsToDifferentEnterprise()
        {
            var userId = Guid.NewGuid();
            var claimEnterpriseId = Guid.NewGuid();
            var persistedEnterpriseId = Guid.NewGuid();

            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            userRepository
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new User
                {
                    Id = userId,
                    EnterpriseId = persistedEnterpriseId,
                    IsActive = true,
                    AccessLevel = (short)EnumAccessLevel.Admin
                });

            var handler = new PermissionAuthorizationHandler(userRepository.Object);
            var requirement = new PermissionRequirement(RbacPermissions.Users.Read);
            var principal = BuildPrincipal(userId.ToString(), claimEnterpriseId.ToString());
            var context = new AuthorizationHandlerContext([requirement], principal, null);

            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
        }

        [Fact]
        public async Task HandleRequirementAsync_Denies_WhenRoleDoesNotHavePermission()
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

            var handler = new PermissionAuthorizationHandler(userRepository.Object);
            var requirement = new PermissionRequirement(RbacPermissions.Orders.Update);
            var principal = BuildPrincipal(userId.ToString(), enterpriseId.ToString());
            var context = new AuthorizationHandlerContext([requirement], principal, null);

            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
        }

        [Fact]
        public async Task HandleRequirementAsync_Allows_WhenPermissionIsGranted()
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
                    AccessLevel = (short)EnumAccessLevel.Supervisor
                });

            var handler = new PermissionAuthorizationHandler(userRepository.Object);
            var requirement = new PermissionRequirement(RbacPermissions.Orders.Read);
            var principal = BuildPrincipal(userId.ToString(), enterpriseId.ToString());
            var context = new AuthorizationHandlerContext([requirement], principal, null);

            await handler.HandleAsync(context);

            Assert.True(context.HasSucceeded);
        }

        private static ClaimsPrincipal BuildPrincipal(string? userIdClaimValue, string? enterpriseIdClaimValue)
        {
            var claims = new List<Claim>();
            if (!string.IsNullOrEmpty(userIdClaimValue))
                claims.Add(new Claim(ClaimTypes.Sid, userIdClaimValue));

            if (!string.IsNullOrEmpty(enterpriseIdClaimValue))
                claims.Add(new Claim(ClaimTypes.GroupSid, enterpriseIdClaimValue));

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }
    }
}
