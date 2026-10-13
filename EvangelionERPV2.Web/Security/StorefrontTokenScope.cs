using System.Security.Claims;

namespace EvangelionERPV2.Web.Security
{
    /// <summary>
    /// Marks a token as issued to the storefront's service account, and confines both ways what it
    /// authorises: such a token reaches only the storefront's service permissions, and those are
    /// reachable by nothing else. They read across tenants, so no role — Admin included — may hold
    /// them through the role map.
    /// </summary>
    public static class StorefrontTokenScope
    {
        public const string ClaimType = PdvTokenScope.ClaimType;
        public const string ClaimValue = Shared.Security.MachineClaims.StorefrontScopeValue;

        public static readonly IReadOnlySet<string> ServicePermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RbacPermissions.Storefront.CatalogRead,
            RbacPermissions.Storefront.Checkout,
            RbacPermissions.Storefront.OrdersRead,
            RbacPermissions.Storefront.OrdersWrite
        };

        public static bool IsStorefrontScoped(ClaimsPrincipal? user) =>
            user?.HasClaim(ClaimType, ClaimValue) == true;

        /// <summary>
        /// Whether this caller may exercise <paramref name="permission"/>, after both the storefront
        /// and the till scopes have had their say.
        /// </summary>
        public static bool Grants(ClaimsPrincipal? user, short accessLevel, string permission)
        {
            if (ServicePermissions.Contains(permission))
                return IsStorefrontScoped(user);

            return !IsStorefrontScoped(user) && PdvTokenScope.Grants(user, accessLevel, permission);
        }
    }
}
