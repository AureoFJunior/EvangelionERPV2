using Microsoft.AspNetCore.Authorization;

namespace EvangelionERPV2.Web.Security
{
    public sealed class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string permission)
        {
            Permission = string.IsNullOrWhiteSpace(permission)
                ? throw new ArgumentException("Permission is required.", nameof(permission))
                : permission;
        }

        public string Permission { get; }
    }
}
