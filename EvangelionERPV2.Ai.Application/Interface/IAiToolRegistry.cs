using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Application.Interface
{
    public interface IAiToolRegistry
    {
        /// <param name="grantedPermissions">
        /// The caller's own permissions, computed by the controller from RbacRolePermissionMap.
        /// Passed as a set rather than a ClaimsPrincipal so this module needs no reference to Web.
        /// </param>
        IReadOnlyList<AIFunction> Build(IReadOnlySet<string> grantedPermissions, AiToolContext context);

        IAiTool? Find(string name);
    }
}
