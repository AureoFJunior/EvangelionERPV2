using System.Reflection;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Every policy named by an <c>[Authorize(Policy = ...)]</c> attribute has to exist in the
    /// container, or the endpoint answers 500 from <c>AuthorizationPolicy.CombineAsync</c> for
    /// every caller — authenticated or not, permitted or not.
    /// </summary>
    public class RbacPolicyRegistrationTests
    {
        [Fact]
        public void EveryPolicyNamedByAnEndpoint_IsRegistered()
        {
            var options = new AuthorizationOptions();
            options.AddRbacPolicies();

            var named = typeof(RbacPolicies).Assembly.GetTypes()
                .Where(type => typeof(ControllerBase).IsAssignableFrom(type) || typeof(Controller).IsAssignableFrom(type))
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Cast<MemberInfo>()
                    .Append(type))
                .SelectMany(member => member.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
                .Select(attribute => attribute.Policy)
                .Where(policy => !string.IsNullOrWhiteSpace(policy))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.NotEmpty(named);

            var missing = named.Where(policy => options.GetPolicy(policy!) == null).ToList();

            Assert.True(
                missing.Count == 0,
                $"Named by an endpoint but never registered: {string.Join(", ", missing)}");
        }

        [Fact]
        public void EveryPermissionInTheRoleMap_IsARegisteredPermission()
        {
            var known = RbacPermissions.All.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var granted = Enum.GetValues<Shared.Enums.EnumAccessLevel>()
                .SelectMany(level => RbacRolePermissionMap.GetPermissions((short)level))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var unknown = granted.Where(permission => !known.Contains(permission)).ToList();

            Assert.True(
                unknown.Count == 0,
                $"Granted to a role but absent from RbacPermissions.All: {string.Join(", ", unknown)}");
        }
    }
}
