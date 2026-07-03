using Microsoft.AspNetCore.Authorization;

namespace EvangelionERPV2.Web.Security
{
    public sealed class SelfOrPermissionRequirement : IAuthorizationRequirement
    {
        public SelfOrPermissionRequirement(string selfPermission, string elevatedPermission, string routeKey)
        {
            SelfPermission = string.IsNullOrWhiteSpace(selfPermission)
                ? throw new ArgumentException("Self permission is required.", nameof(selfPermission))
                : selfPermission;

            ElevatedPermission = string.IsNullOrWhiteSpace(elevatedPermission)
                ? throw new ArgumentException("Elevated permission is required.", nameof(elevatedPermission))
                : elevatedPermission;

            RouteKey = string.IsNullOrWhiteSpace(routeKey)
                ? throw new ArgumentException("Route key is required.", nameof(routeKey))
                : routeKey;
        }

        public string SelfPermission { get; }

        public string ElevatedPermission { get; }

        public string RouteKey { get; }
    }
}
