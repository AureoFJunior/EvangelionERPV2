using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Web.Security;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The confinement that makes a till token safe to leave on a shop floor. Both authorization
    /// handlers resolve permissions from the persisted <c>AccessLevel</c>, so without this a
    /// supervisor's till token would open everything that supervisor can open.
    /// </summary>
    public class PdvTokenScopeTests
    {
        private const short Supervisor = (short)EnumAccessLevel.Supervisor;

        [Fact]
        public void Grants_RefusesNonPdvPermissions_ToATillToken()
        {
            var till = Principal(new Claim(PdvTokenScope.ClaimType, PdvTokenScope.ClaimValue));

            Assert.True(RbacRolePermissionMap.HasPermission(Supervisor, RbacPermissions.Bills.Read));
            Assert.False(PdvTokenScope.Grants(till, Supervisor, RbacPermissions.Bills.Read));
        }

        [Fact]
        public void Grants_AllowsPdvPermissions_ToATillToken()
        {
            var till = Principal(new Claim(PdvTokenScope.ClaimType, PdvTokenScope.ClaimValue));

            Assert.True(PdvTokenScope.Grants(till, Supervisor, RbacPermissions.Pdv.VoidItem));
        }

        [Fact]
        public void Grants_LeavesAnOrdinaryTokenAlone()
        {
            var web = Principal();

            Assert.True(PdvTokenScope.Grants(web, Supervisor, RbacPermissions.Bills.Read));
        }

        [Fact]
        public void Grants_StillRefusesWhatTheRoleNeverHad()
        {
            // The scope narrows; it never widens. An employee's till token gets employee grants.
            var till = Principal(new Claim(PdvTokenScope.ClaimType, PdvTokenScope.ClaimValue));

            Assert.False(PdvTokenScope.Grants(till, (short)EnumAccessLevel.Employee, RbacPermissions.Pdv.VoidItem));
        }

        [Fact]
        public void PermissionsFor_ReturnsOnlyPdvGrants()
        {
            var permissions = PdvTokenScope.PermissionsFor(Supervisor);

            Assert.NotEmpty(permissions);
            Assert.All(permissions, permission => Assert.StartsWith(PdvTokenScope.PermissionPrefix, permission));
        }

        private static ClaimsPrincipal Principal(params Claim[] claims) =>
            new(new ClaimsIdentity(claims, "Test"));
    }
}
