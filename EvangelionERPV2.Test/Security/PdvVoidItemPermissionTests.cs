using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Web.Security;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Voiding a line mid-sale is supervisor tier. It is the commonest shrinkage route at a till:
    /// scan the item, take the customer's cash, remove the line before the sale is recorded.
    /// </summary>
    public class PdvVoidItemPermissionTests
    {
        [Fact]
        public void VoidItem_IsRegisteredInAll()
        {
            Assert.Contains(RbacPermissions.Pdv.VoidItem, RbacPermissions.All);
        }

        [Fact]
        public void VoidItem_IsSupervisorTier_NotEmployeeTier()
        {
            Assert.False(
                RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Employee, RbacPermissions.Pdv.VoidItem),
                "An operator who can void their own lines is an operator who can pocket the difference.");

            Assert.True(
                RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Supervisor, RbacPermissions.Pdv.VoidItem));
        }

        [Fact]
        public void VoidItem_IsGrantedToManagerAndAdminToo()
        {
            Assert.True(
                RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Manager, RbacPermissions.Pdv.VoidItem));

            Assert.True(
                RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Admin, RbacPermissions.Pdv.VoidItem));
        }
    }
}
