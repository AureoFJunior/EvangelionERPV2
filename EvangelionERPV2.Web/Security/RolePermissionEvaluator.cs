using System.Security.Claims;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Web.Security
{
    /// <summary>Access from <see cref="User.AccessLevel"/> through <see cref="RbacRolePermissionMap"/>, narrowed by the token's scope.</summary>
    public sealed class RolePermissionEvaluator : IPermissionEvaluator
    {
        private static readonly string[] Catalog = [.. RbacPermissions.All.Union(StorefrontTokenScope.ServicePermissions, StringComparer.OrdinalIgnoreCase)];

        public Task<PermissionDecision> DecideAsync(ClaimsPrincipal principal, User user, string permission) =>
            Task.FromResult(StorefrontTokenScope.Grants(principal, user.AccessLevel, permission)
                ? PermissionDecision.Allowed
                : PermissionDecision.Denied);

        public Task<IReadOnlySet<string>> EffectiveAsync(ClaimsPrincipal principal, User user) =>
            Task.FromResult<IReadOnlySet<string>>(Catalog
                .Where(permission => StorefrontTokenScope.Grants(principal, user.AccessLevel, permission))
                .ToHashSet(StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlySet<string>> HoldsAsync(ClaimsPrincipal principal, User user) => EffectiveAsync(principal, user);

        public Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> GrantedAsync(IReadOnlyCollection<User> users) =>
            Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlySet<string>>>(users
                .DistinctBy(user => user.Id)
                .ToDictionary(
                    user => user.Id,
                    user => (IReadOnlySet<string>)RbacRolePermissionMap.GetPermissions(user.AccessLevel).ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }
}
