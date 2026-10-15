using System.Reflection;
using System.Security.Claims;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Web.Security;
using Moq;
using Microsoft.AspNetCore.Authorization;

namespace EvangelionERPV2.Test.Security
{
    internal static class ControllerPolicyTestHelper
    {
        public static IPermissionEvaluator RolePermissionEvaluator()
        {
            var evaluator = new Mock<IPermissionEvaluator>();
            evaluator.Setup(x => x.GrantedAsync(It.IsAny<IReadOnlyCollection<User>>()))
                .ReturnsAsync((IReadOnlyCollection<User> users) => (IReadOnlyDictionary<Guid, IReadOnlySet<string>>)users
                    .ToDictionary(user => user.Id, user => (IReadOnlySet<string>)RbacRolePermissionMap.GetPermissions(user.AccessLevel)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)));
            evaluator.Setup(x => x.EffectiveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<User>()))
                .ReturnsAsync((ClaimsPrincipal principal, User user) => (IReadOnlySet<string>)RbacRolePermissionMap
                    .GetPermissions(user.AccessLevel)
                    .Where(permission => StorefrontTokenScope.AllowsThroughToken(principal, permission))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));
            return evaluator.Object;
        }

        public static void AssertActionPolicy<TController>(string actionName, string expectedPolicy)
        {
            var method = typeof(TController)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(x => x.Name == actionName);

            var actionPolicy = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .FirstOrDefault(x => x.Policy == expectedPolicy);
            var controllerPolicy = typeof(TController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .FirstOrDefault(x => x.Policy == expectedPolicy);

            Assert.True(
                actionPolicy != null || controllerPolicy != null,
                $"Expected {typeof(TController).Name}.{actionName} to require policy '{expectedPolicy}'.");
        }
    }
}
