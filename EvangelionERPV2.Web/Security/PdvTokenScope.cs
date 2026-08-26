using System.Security.Claims;

namespace EvangelionERPV2.Web.Security
{
    /// <summary>
    /// Marks a token as issued to a till, and confines what such a token can authorise.
    /// </summary>
    /// <remarks>
    /// Both authorization handlers derive permissions from the persisted <c>AccessLevel</c>, not
    /// from claims — deliberately, so a stale token cannot outlive a demotion. The consequence is
    /// that listing <c>pdv.*</c> permissions in a token would scope nothing at all: a supervisor's
    /// till token would still open payroll, because the handler never reads that list.
    ///
    /// So the scope is one claim the handlers actually check. A credential lifted off a shop-floor
    /// machine buys the attacker the till, and only the till.
    /// </remarks>
    public static class PdvTokenScope
    {
        public const string ClaimType = "token_scope";
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
