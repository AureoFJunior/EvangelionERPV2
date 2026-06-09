using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;

namespace EvangelionERPV2.Test.Security
{
    public class ReportsControllerManagementAccessTests
    {
        [Fact]
        public void ReportsEndpoints_RequireManagementPolicies()
        {
            ControllerPolicyTestHelper.AssertActionPolicy<ReportsController>(
                nameof(ReportsController.GetReports),
                "rbac:" + RbacPermissions.Reports.Read);
            ControllerPolicyTestHelper.AssertActionPolicy<ReportsController>(
                nameof(ReportsController.GenerateReport),
                "rbac:" + RbacPermissions.Reports.Generate);
            ControllerPolicyTestHelper.AssertActionPolicy<ReportsController>(
                nameof(ReportsController.GetReportById),
                "rbac:" + RbacPermissions.Reports.Read);
        }
    }
}
