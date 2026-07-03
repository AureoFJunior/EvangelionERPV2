using System.Security.Claims;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Http;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    public class ActiveTenantEnforcementMiddlewareTests
    {
        [Fact]
        public async Task InvokeAsync_WhenEnterpriseIsInactive_ReturnsUnauthorizedAndStopsPipeline()
        {
            var userId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var context = BuildAuthenticatedContext(userId, enterpriseId);

            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            userRepository
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new User
                {
                    Id = userId,
                    EnterpriseId = enterpriseId,
                    IsActive = true
                });

            var enterpriseRepository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict);
            enterpriseRepository
                .Setup(x => x.GetByIdAsync(enterpriseId))
                .ReturnsAsync(new Enterprise
                {
                    Id = enterpriseId,
                    IsActive = false
                });

            var nextCalled = false;
            var middleware = new ActiveTenantEnforcementMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context, userRepository.Object, enterpriseRepository.Object);

            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
            Assert.False(nextCalled);
        }

        [Fact]
        public async Task InvokeAsync_WhenUserAndEnterpriseAreActive_ContinuesPipeline()
        {
            var userId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var context = BuildAuthenticatedContext(userId, enterpriseId);

            var userRepository = new Mock<IRepository<User>>(MockBehavior.Strict);
            userRepository
                .Setup(x => x.GetByIdAsync(userId))
                .ReturnsAsync(new User
                {
                    Id = userId,
                    EnterpriseId = enterpriseId,
                    IsActive = true
                });

            var enterpriseRepository = new Mock<IRepository<Enterprise>>(MockBehavior.Strict);
            enterpriseRepository
                .Setup(x => x.GetByIdAsync(enterpriseId))
                .ReturnsAsync(new Enterprise
                {
                    Id = enterpriseId,
                    IsActive = true
                });

            var nextCalled = false;
            var middleware = new ActiveTenantEnforcementMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context, userRepository.Object, enterpriseRepository.Object);

            Assert.True(nextCalled);
        }

        private static DefaultHttpContext BuildAuthenticatedContext(Guid userId, Guid enterpriseId)
        {
            var context = new DefaultHttpContext();
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Sid, userId.ToString()),
                new Claim("uid", userId.ToString()),
                new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())
            }, authenticationType: "Bearer"));

            return context;
        }
    }
}
