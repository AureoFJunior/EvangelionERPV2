using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Web.Security;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// A seller seeing their own row. The screen that decides someone's commission was Manager-only,
    /// which left the person being measured unable to see the measurement.
    /// </summary>
    public class SalesPerformanceSelfReadTests
    {
        [Fact]
        public void ReadSelf_IsInTheListThatBuildsThePolicies()
        {
            Assert.Contains(RbacPermissions.SalesPerformance.ReadSelf, RbacPermissions.All);
        }

        [Fact]
        public void ReadSelf_StartsAtTheBaseRole()
        {
            Assert.True(RbacRolePermissionMap.HasPermission(
                (short)EnumAccessLevel.Employee, RbacPermissions.SalesPerformance.ReadSelf));
        }

        [Fact]
        public void ReadingEveryonesRow_StaysAtManager()
        {
            Assert.False(RbacRolePermissionMap.HasPermission(
                (short)EnumAccessLevel.Employee, RbacPermissions.SalesPerformance.Read));
            Assert.False(RbacRolePermissionMap.HasPermission(
                (short)EnumAccessLevel.Supervisor, RbacPermissions.SalesPerformance.Read));
            Assert.True(RbacRolePermissionMap.HasPermission(
                (short)EnumAccessLevel.Manager, RbacPermissions.SalesPerformance.Read));
        }
    }
}
