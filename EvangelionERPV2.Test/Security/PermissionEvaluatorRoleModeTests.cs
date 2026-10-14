using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Security;
using EvangelionERPV2.Web.Security;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security
{
    // SPEC-iam-zero-trust IAM-0: the evaluator is the old role lookup, decision for decision.
    public class PermissionEvaluatorRoleModeTests
    {
        private readonly RolePermissionEvaluator _evaluator = new();

        private static ClaimsPrincipal Token(string? scope = null) =>
            new(new ClaimsIdentity(scope == null ? [] : [new Claim(MachineClaims.TokenScope, scope)], "test"));

        public static TheoryData<short, string?> RolesAndScopes()
        {
            var data = new TheoryData<short, string?>();
            foreach (var role in Enum.GetValues<EnumAccessLevel>())
                foreach (var scope in new[] { null, PdvTokenScope.ClaimValue, StorefrontTokenScope.ClaimValue })
                    data.Add((short)role, scope);
            return data;
        }

        [Theory]
        [MemberData(nameof(RolesAndScopes))]
        public async Task EveryDecision_MatchesTheRoleMapThroughTheTokenScope(short role, string? scope)
        {
            var user = new User { Id = Guid.NewGuid(), AccessLevel = role };
            var token = Token(scope);
            var catalog = RbacPermissions.All.Union(StorefrontTokenScope.ServicePermissions).ToList();

            foreach (var permission in catalog)
            {
                var expected = StorefrontTokenScope.Grants(token, role, permission);
                Assert.Equal(expected ? PermissionDecision.Allowed : PermissionDecision.Denied, await _evaluator.DecideAsync(token, user, permission));
            }

            var effective = await _evaluator.EffectiveAsync(token, user);
            Assert.Equal(catalog.Where(p => StorefrontTokenScope.Grants(token, role, p)).OrderBy(p => p), effective.OrderBy(p => p));
        }

        [Fact]
        public async Task Granted_IsEachUsersRoleSetRegardlessOfToken()
        {
            var cashier = new User { Id = Guid.NewGuid(), AccessLevel = (short)EnumAccessLevel.Employee };
            var admin = new User { Id = Guid.NewGuid(), AccessLevel = (short)EnumAccessLevel.Admin };

            var granted = await _evaluator.GrantedAsync([cashier, admin, cashier]);

            Assert.Equal(2, granted.Count);
            Assert.Equal(RbacRolePermissionMap.GetPermissions(cashier.AccessLevel).OrderBy(p => p), granted[cashier.Id].OrderBy(p => p));
            Assert.Equal(PdvTokenScope.PermissionsFor(admin.AccessLevel), PdvTokenScope.PermissionsIn(granted[admin.Id]));
        }

        [Fact]
        public async Task AnUnknownPermission_IsDenied()
        {
            var admin = new User { Id = Guid.NewGuid(), AccessLevel = (short)EnumAccessLevel.Admin };

            Assert.Equal(PermissionDecision.Denied, await _evaluator.DecideAsync(Token(), admin, "nothing.such"));
        }
    }
}
