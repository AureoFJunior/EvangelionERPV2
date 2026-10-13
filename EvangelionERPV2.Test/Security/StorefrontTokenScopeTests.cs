using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Web.Security;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The storefront's service permissions read across every tenant, so they must be reachable by the
    /// storefront token and by nothing else — not even an Admin, whose role map holds every permission.
    /// </summary>
    public class StorefrontTokenScopeTests
    {
        private const short Admin = (short)EnumAccessLevel.Admin;

        public static TheoryData<string> ServicePermissions =>
        [
            RbacPermissions.Storefront.CatalogRead,
            RbacPermissions.Storefront.Checkout,
            RbacPermissions.Storefront.OrdersRead,
            RbacPermissions.Storefront.OrdersWrite
        ];

        [Theory]
        [MemberData(nameof(ServicePermissions))]
        public void Grants_RefusesServicePermissions_ToAnAdminWebToken(string permission)
        {
            Assert.True(RbacRolePermissionMap.HasPermission(Admin, permission));
            Assert.False(StorefrontTokenScope.Grants(Principal(), Admin, permission));
        }

        [Theory]
        [MemberData(nameof(ServicePermissions))]
        public void Grants_RefusesServicePermissions_ToATillToken(string permission)
        {
            var till = Principal(new Claim(PdvTokenScope.ClaimType, PdvTokenScope.ClaimValue));

            Assert.False(StorefrontTokenScope.Grants(till, Admin, permission));
        }

        [Theory]
        [MemberData(nameof(ServicePermissions))]
        public void Grants_AllowsServicePermissions_ToTheStorefrontToken_WhateverItsRole(string permission)
        {
            Assert.True(StorefrontTokenScope.Grants(Storefront(), (short)EnumAccessLevel.Employee, permission));
        }

        [Theory]
        [InlineData(RbacPermissions.Storefront.Manage)]
        [InlineData(RbacPermissions.Orders.Read)]
        [InlineData(RbacPermissions.Products.Read)]
        [InlineData(RbacPermissions.Pdv.Operate)]
        public void Grants_RefusesEverythingElse_ToTheStorefrontToken(string permission)
        {
            Assert.False(StorefrontTokenScope.Grants(Storefront(), Admin, permission));
        }

        [Fact]
        public void Grants_LeavesStaffPermissionsToTheRoleMap()
        {
            Assert.True(StorefrontTokenScope.Grants(Principal(), (short)EnumAccessLevel.Manager, RbacPermissions.Storefront.Manage));
            Assert.False(StorefrontTokenScope.Grants(Principal(), (short)EnumAccessLevel.Supervisor, RbacPermissions.Storefront.Manage));
        }

        [Fact]
        public void Grants_KeepsTheTillScope()
        {
            var till = Principal(new Claim(PdvTokenScope.ClaimType, PdvTokenScope.ClaimValue));

            Assert.False(StorefrontTokenScope.Grants(till, Admin, RbacPermissions.Bills.Read));
        }

        private static ClaimsPrincipal Storefront() =>
            Principal(new Claim(StorefrontTokenScope.ClaimType, StorefrontTokenScope.ClaimValue));

        private static ClaimsPrincipal Principal(params Claim[] claims) =>
            new(new ClaimsIdentity(claims, "Test"));
    }
}
