using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;
using EvangelionERPV2.Web.Security.Iam;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;

namespace EvangelionERPV2.Test.Security.Iam
{
    // SPEC-iam-zero-trust §3: no permission ships without a label and a tier, and the IAM endpoints are gated.
    public class IamCatalogTests
    {
        [Fact]
        public void EveryPermission_HasALabelAndAModuleLabel()
        {
            foreach (var permission in RbacPermissions.All.Union(StorefrontTokenScope.ServicePermissions))
            {
                Assert.True(IamCatalog.LabelOf(permission) != null, $"{permission} has no label in IamCatalog.");
                Assert.True(IamCatalog.ModuleLabelOf(IamCatalog.ModuleOf(permission)) != null, $"{permission}'s module has no label.");
            }
        }

        [Fact]
        public void EveryAuthorizePolicyPermission_IsInTheCatalog()
        {
            var policies = typeof(IamController).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods().Cast<MemberInfo>().Append(t))
                .SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>())
                .Select(a => a.Policy)
                .Where(p => p != null && p.StartsWith("rbac:", StringComparison.Ordinal) && RbacPolicies.PermissionToPolicyName.Values.Contains(p))
                .Select(p => p![5..])
                .Distinct();

            Assert.All(policies, permission => Assert.True(IamCatalog.ByPermission.ContainsKey(permission), permission));
        }

        [Fact]
        public void TheLimenServiceSetAndTheBaseline_AreNotGrantable()
        {
            Assert.All(StorefrontTokenScope.ServicePermissions, p => Assert.False(IamCatalog.IsGrantable(p)));
            Assert.All(IamCatalog.Baseline, p => Assert.False(IamCatalog.IsGrantable(p)));
        }

        [Theory]
        [InlineData(RbacPermissions.Iam.Manage, PermissionTier.Privileged)]
        [InlineData(RbacPermissions.Payroll.Read, PermissionTier.Privileged)]
        [InlineData(RbacPermissions.EmployeeAbsences.Read, PermissionTier.Privileged)]
        [InlineData(RbacPermissions.Accounting.Export, PermissionTier.Sensitive)]
        [InlineData(RbacPermissions.Ai.Act, PermissionTier.Sensitive)]
        [InlineData(RbacPermissions.Pdv.OverrideDiscount, PermissionTier.Standard)]
        [InlineData(RbacPermissions.Products.Read, PermissionTier.Standard)]
        public void Tiers_FollowTheSpec(string permission, PermissionTier tier) => Assert.Equal(tier, IamCatalog.TierOf(permission));

        [Theory]
        [InlineData(nameof(IamController.Catalog), RbacPolicies.Iam.Read)]
        [InlineData(nameof(IamController.Access), RbacPolicies.Iam.Read)]
        [InlineData(nameof(IamController.Holders), RbacPolicies.Iam.Read)]
        [InlineData(nameof(IamController.Reviews), RbacPolicies.Iam.Read)]
        [InlineData(nameof(IamController.StepUp), RbacPolicies.Users.ReadSelf)]
        [InlineData(nameof(IamController.EndStepUp), RbacPolicies.Users.ReadSelf)]
        [InlineData(nameof(IamController.Grants), RbacPolicies.Iam.Manage)]
        [InlineData(nameof(IamController.ApplyTemplate), RbacPolicies.Iam.Manage)]
        [InlineData(nameof(IamController.Switch), RbacPolicies.Iam.Manage)]
        [InlineData(nameof(IamController.Me), RbacPolicies.Users.ReadSelf)]
        public void IamEndpoints_NeedTheirPermission(string action, string policy) =>
            Assert.Equal(policy, typeof(IamController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }
}
