using System.Security.Claims;

namespace EvangelionERPV2.Web.Security
{
    /// <summary>
    /// Marks a token as issued to a till, and confines what such a token can authorise.
    /// </summary>
    public static class PdvTokenScope
    {
        public const string ClaimType = Shared.Security.MachineClaims.TokenScope;
        public const string ClaimValue = "pdv";

        /// <summary>Prefix every permission a till may exercise shares. See <c>RbacPermissions.Pdv</c>.</summary>
        public const string PermissionPrefix = "pdv.";

        public static bool IsPdvScoped(ClaimsPrincipal? user) =>
            user?.HasClaim(ClaimType, ClaimValue) == true;

        /// <summary>
        /// The <c>pdv.*</c> subset of a role's grants — what a till is told it may do, and the only
        /// thing its token can reach.
        /// </summary>
        public static IReadOnlyList<string> PermissionsFor(short accessLevel) =>
            RbacRolePermissionMap.GetPermissions(accessLevel)
                .Where(permission => permission.StartsWith(PermissionPrefix, StringComparison.Ordinal))
                .OrderBy(permission => permission, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// Whether this caller's role grants <paramref name="permission"/>, after the token's own
        /// scope has had its say. A non-till token is unaffected.
        /// </summary>
        public static bool Grants(ClaimsPrincipal? user, short accessLevel, string permission)
        {
            if (IsPdvScoped(user) && !permission.StartsWith(PermissionPrefix, StringComparison.Ordinal))
                return false;

            return RbacRolePermissionMap.HasPermission(accessLevel, permission);
        }
    }
}
