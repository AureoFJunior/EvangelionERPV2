using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;

namespace EvangelionERPV2.Test.Security
{
    public class OpportunitiesControllerManagementAccessTests
    {
        [Fact]
        public void OpportunitiesEndpoints_RequireManagerPolicies()
        {
            ControllerPolicyTestHelper.AssertActionPolicy<OpportunitiesController>(
                nameof(OpportunitiesController.GetOpportunities),
                "rbac:" + RbacPermissions.Opportunities.Read);
            ControllerPolicyTestHelper.AssertActionPolicy<OpportunitiesController>(
                nameof(OpportunitiesController.GetOpportunity),
                "rbac:" + RbacPermissions.Opportunities.Read);
            ControllerPolicyTestHelper.AssertActionPolicy<OpportunitiesController>(
                nameof(OpportunitiesController.GetSummary),
                "rbac:" + RbacPermissions.Opportunities.Read);
            ControllerPolicyTestHelper.AssertActionPolicy<OpportunitiesController>(
                nameof(OpportunitiesController.AddFeedback),
                "rbac:" + RbacPermissions.Opportunities.Feedback);
            ControllerPolicyTestHelper.AssertActionPolicy<OpportunitiesController>(
                nameof(OpportunitiesController.Recompute),
                "rbac:" + RbacPermissions.Opportunities.Recompute);
        }
    }
}
