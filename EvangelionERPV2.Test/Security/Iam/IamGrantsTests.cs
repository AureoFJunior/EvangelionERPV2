using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Security;
using EvangelionERPV2.Web.Security;
using EvangelionERPV2.Web.Security.Iam;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.UserModule.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security.Iam
{
    // SPEC-iam-zero-trust IAM-1: zero trust by default, per-user grants, the write rules and the switch.
    public class IamGrantsTests : IDisposable
    {
        private readonly IamTestDatabase _database = new();
        private readonly MemoryDistributedCache _cache = new(Options.Create(new MemoryDistributedCacheOptions()));
        private readonly MovableTime _time = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        private readonly Enterprise _enterprise = new() { Id = Guid.NewGuid(), Name = "Loja", IsActive = true };
        private readonly User _admin;
        private readonly User _clerk;

        public IamGrantsTests()
        {
            _admin = NewUser("Ana", EnumAccessLevel.Admin, mfa: true);
            _clerk = NewUser("Caio", EnumAccessLevel.Employee);
            _database.SeedAsync(_enterprise, _admin, _clerk).GetAwaiter().GetResult();
        }

        public void Dispose() => _database.Dispose();

        private User NewUser(string name, EnumAccessLevel level, bool mfa = false, Guid? enterprise = null) => new()
        {
            Id = Guid.NewGuid(),
            FirstName = name,
            LastName = "Silva",
            UserName = name.ToLowerInvariant(),
            Email = $"{name.ToLowerInvariant()}@loja.com",
            Password = "x",
            AccessLevel = (short)level,
            EnterpriseId = enterprise ?? _enterprise.Id,
            IsActive = true,
            MfaEnabled = mfa
        };

        private static ClaimsPrincipal Token(User user, string? scope = null)
        {
            var claims = new List<Claim> { new(ClaimTypes.Sid, user.Id.ToString()), new(ClaimTypes.GroupSid, user.EnterpriseId.ToString()!) };
            if (scope != null)
                claims.Add(new Claim(MachineClaims.TokenScope, scope));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        private PermissionEvaluator Evaluator() => new(_database.CreateContext(), _cache, _time);

        private IamService Service()
        {
            var db = _database.CreateContext();
            return new IamService(db, new AuditTrailWriter(db), _cache, new Repository<Enterprise>(db), _time);
        }

        /// <summary>The user as the next request's handler would read it.</summary>
        private User Fresh(User user)
        {
            using var db = _database.CreateContext();
            return db.User.AsNoTracking().Single(u => u.Id == user.Id);
        }

        /// <summary>Whether the user holds it through this token; step-up is IamStepUpTests' business.</summary>
        private async Task<bool> Allowed(User user, string permission, string? scope = null) =>
            (await Evaluator().HoldsAsync(Token(user, scope), Fresh(user))).Contains(permission);

        private Task Grant(User target, params string[] permissions) =>
            Service().ApplyAsync(Fresh(_admin), target.Id,
                permissions.Select(p => new GrantChange(p, null, GrantState.Allow, null, "teste")).ToList());

        private IamController ControllerFor(User caller)
        {
            var db = _database.CreateContext();
            var mfa = new MfaService(new Repository<User>(db), new Moq.Mock<Shared.Repositories.IRepository<MfaRecoveryCode>>().Object,
                new Moq.Mock<Shared.Repositories.IRepository<RefreshToken>>().Object, new Moq.Mock<Shared.Auditing.IAuditTrailWriter>().Object, _cache, _time);
            return new IamController(new IamService(db, new AuditTrailWriter(db), _cache, new Repository<Enterprise>(db), _time),
                Evaluator(), new Repository<User>(db), mfa, _cache, _time)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([
                            new Claim(ClaimTypes.Sid, caller.Id.ToString()),
                            new Claim(ClaimTypes.GroupSid, caller.EnterpriseId!.Value.ToString())
                        ], "test"))
                    }
                }
            };
        }

        [Fact]
        public async Task Grants_WithNullBody_ReturnsBadRequest()
        {
            var result = await ControllerFor(_admin).Grants(_clerk.Id, null!);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ApplyTemplate_WithNullBody_ReturnsBadRequest()
        {
            var result = await ControllerFor(_admin).ApplyTemplate(_clerk.Id, null!);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Switch_WithNullBody_ReturnsBadRequest()
        {
            var result = await ControllerFor(_admin).Switch(null!);
            Assert.IsType<BadRequestObjectResult>(result);
        }

        private async Task SwitchToGrants() => await Service().SwitchAsync(Fresh(_admin), EnumIamMode.Grants);

        private static async Task<IamException> Refused(Func<Task> act) => await Assert.ThrowsAsync<IamException>(act);

        [Fact]
        public async Task InGrantsMode_AUserWithNoGrants_HoldsOnlyTheBaseline()
        {
            await SwitchToGrants();

            Assert.True(await Allowed(_clerk, RbacPermissions.Users.ReadSelf));
            Assert.False(await Allowed(_clerk, RbacPermissions.Orders.Read));
            Assert.Equal(IamCatalog.Baseline.OrderBy(p => p), (await Evaluator().EffectiveAsync(Token(_clerk), Fresh(_clerk))).OrderBy(p => p));
        }

        private Task Set(User target, string permission, GrantState state, string? reason = "teste") =>
            Service().ApplyAsync(Fresh(_admin), target.Id, [new GrantChange(permission, null, state, null, reason)]);

        [Fact]
        public async Task InRoleMode_AnAllowAddsToTheRole_ADenyTakesAway_AndInheritRestores()
        {
            Assert.True(await Allowed(_clerk, RbacPermissions.Orders.Read));
            Assert.False(await Allowed(_clerk, RbacPermissions.Bills.Settle));

            await Set(_clerk, RbacPermissions.Bills.Settle, GrantState.Allow);
            await Set(_clerk, RbacPermissions.Orders.Read, GrantState.Deny);

            Assert.True(await Allowed(_clerk, RbacPermissions.Bills.Settle));
            Assert.False(await Allowed(_clerk, RbacPermissions.Orders.Read));
            Assert.True(await Allowed(_clerk, RbacPermissions.Orders.Create));

            var leaves = (await Service().GetAccessAsync(_enterprise.Id, _clerk.Id)).Modules.SelectMany(m => m.Permissions).ToDictionary(p => p.Permission);
            Assert.Equal(("allow", true, false), (leaves[RbacPermissions.Bills.Settle].Override, leaves[RbacPermissions.Bills.Settle].Effective, leaves[RbacPermissions.Bills.Settle].HeldByRole));
            Assert.Equal(("deny", false, true), (leaves[RbacPermissions.Orders.Read].Override, leaves[RbacPermissions.Orders.Read].Effective, leaves[RbacPermissions.Orders.Read].HeldByRole));
            Assert.Equal((null, true), (leaves[RbacPermissions.Orders.Create].Override, leaves[RbacPermissions.Orders.Create].Effective));

            await Set(_clerk, RbacPermissions.Orders.Read, GrantState.Inherit);
            Assert.True(await Allowed(_clerk, RbacPermissions.Orders.Read));
        }

        [Theory]
        [InlineData(EnumAccessLevel.Admin)]
        [InlineData(EnumAccessLevel.Manager)]
        [InlineData(EnumAccessLevel.Supervisor)]
        [InlineData(EnumAccessLevel.Employee)]
        public async Task InRoleMode_WithoutChanges_AUserHoldsExactlyTheirRole(EnumAccessLevel level)
        {
            var user = NewUser("Duda", level);
            await _database.SeedAsync(user);

            var held = await Evaluator().HoldsAsync(Token(user), Fresh(user));

            var expected = RbacRolePermissionMap.GetPermissions((short)level)
                .Where(p => StorefrontTokenScope.AllowsThroughToken(Token(user), p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(expected.OrderBy(p => p), held.OrderBy(p => p));
        }

        [Fact]
        public async Task InRoleMode_DenyingTheLastAdminsIamManage_IsRefused()
        {
            var deputy = NewUser("Bia", EnumAccessLevel.Manager, mfa: true);
            await _database.SeedAsync(deputy);
            await Service().ApplyAsync(Fresh(_admin), deputy.Id,
                [new GrantChange(RbacPermissions.Iam.Manage, null, GrantState.Allow, _time.GetUtcNow().UtcDateTime.AddDays(1), "cobrir férias")]);

            var error = await Refused(() => Service().ApplyAsync(Fresh(deputy), _admin.Id,
                [new GrantChange(RbacPermissions.Iam.Manage, null, GrantState.Deny, null, null)]));

            Assert.Equal("last_iam_manager", error.Code);
            Assert.True(await Allowed(_admin, RbacPermissions.Iam.Manage));
        }

        [Fact]
        public async Task DeactivatingTheLastAccessManager_IsRefusedAndRolledBack()
        {
            var error = await Refused(() => Service().DeactivateUserAsync(_enterprise.Id, _admin.Id));

            Assert.Equal("last_iam_manager", error.Code);
            Assert.True(Fresh(_admin).IsActive);
        }

        [Fact]
        public async Task SwitchingToZeroTrust_IgnoresDenies_AndKeepsOnlyAllows()
        {
            await Set(_clerk, RbacPermissions.Orders.Read, GrantState.Deny);
            await Set(_clerk, RbacPermissions.Bills.Settle, GrantState.Allow);

            await SwitchToGrants();

            Assert.True(await Allowed(_clerk, RbacPermissions.Bills.Settle));
            Assert.False(await Allowed(_clerk, RbacPermissions.Orders.Read));
            Assert.False(await Allowed(_clerk, RbacPermissions.Orders.Create));
        }

        [Fact]
        public async Task AGrant_WorksOnTheNextRequest_AndARevokeStopsItOnTheNextRequest()
        {
            await SwitchToGrants();
            Assert.False(await Allowed(_clerk, RbacPermissions.Orders.Read));

            await Grant(_clerk, RbacPermissions.Orders.Read);
            Assert.True(await Allowed(_clerk, RbacPermissions.Orders.Read));

            await Service().ApplyAsync(Fresh(_admin), _clerk.Id, [new GrantChange(RbacPermissions.Orders.Read, null, GrantState.Inherit, null, null)]);
            Assert.False(await Allowed(_clerk, RbacPermissions.Orders.Read));
        }

        [Fact]
        public async Task ATimeBoxedGrant_EndsAtItsExpiry()
        {
            await SwitchToGrants();
            await Service().ApplyAsync(Fresh(_admin), _clerk.Id,
                [new GrantChange(RbacPermissions.Bills.Settle, null, GrantState.Allow, _time.GetUtcNow().UtcDateTime.AddMinutes(30), "fechamento do mês")]);

            Assert.True(await Allowed(_clerk, RbacPermissions.Bills.Settle));
            _time.Advance(TimeSpan.FromMinutes(31));
            Assert.False(await Allowed(_clerk, RbacPermissions.Bills.Settle));
        }

        [Fact]
        public async Task AModuleGrant_ExpandsToItsPermissionsAtWriteTime()
        {
            await Service().ApplyAsync(Fresh(_admin), _clerk.Id, [new GrantChange(null, "products", GrantState.Allow, null, "estoquista")]);

            using var db = _database.CreateContext();
            var stored = db.UserPermissionGrant.Where(g => g.UserId == _clerk.Id).Select(g => g.Permission).ToList();
            Assert.Equal(IamCatalog.ExpandModule("products").OrderBy(p => p), stored.OrderBy(p => p));
        }

        [Fact]
        public async Task AGrantChange_CannotNameBothAPermissionAndAModule()
        {
            var error = await Refused(() => Service().ApplyAsync(Fresh(_admin), _clerk.Id,
                [new GrantChange(RbacPermissions.Orders.Read, "orders", GrantState.Deny, null, null)]));
            Assert.Equal("invalid_grant_target", error.Code);
            using var db = _database.CreateContext();
            Assert.Empty(db.UserPermissionGrant.Where(g => g.UserId == _clerk.Id));
        }

        [Fact]
        public async Task RenewingAnExpiredGrant_PreservesTheOldRowAndStartsFreshUsageHistory()
        {
            var permission = RbacPermissions.Orders.Read;
            await Service().ApplyAsync(Fresh(_admin), _clerk.Id,
                [new GrantChange(permission, null, GrantState.Allow, _time.GetUtcNow().UtcDateTime.AddHours(1), "temporary")]);
            using (var db = _database.CreateContext())
                await db.UserPermissionGrant.Where(g => g.UserId == _clerk.Id && g.Permission == permission)
                    .ExecuteUpdateAsync(set => set.SetProperty(g => g.LastUsedAt, _time.GetUtcNow().UtcDateTime.AddMinutes(20)));
            var oldCreatedAt = DateTime.UtcNow.AddDays(-10);
            using (var db = _database.CreateContext())
                await db.UserPermissionGrant.Where(g => g.UserId == _clerk.Id && g.Permission == permission)
                    .ExecuteUpdateAsync(set => set.SetProperty(g => g.CreatedAt, oldCreatedAt));
            _time.Advance(TimeSpan.FromHours(2));
            var renewedAt = _time.GetUtcNow().UtcDateTime;
            await Service().ApplyAsync(Fresh(_admin), _clerk.Id,
                [new GrantChange(permission, null, GrantState.Allow, null, "renewed")]);
            using var verify = _database.CreateContext();
            var rows = verify.UserPermissionGrant.AsNoTracking().Where(g => g.UserId == _clerk.Id && g.Permission == permission).OrderBy(g => g.CreatedAt).ToList();
            Assert.Equal(2, rows.Count);
            Assert.NotNull(rows[0].RevokedAt);
            Assert.Equal(oldCreatedAt, rows[0].CreatedAt);
            Assert.NotNull(rows[0].LastUsedAt);
            Assert.Null(rows[1].RevokedAt);
            Assert.Equal(renewedAt, rows[1].CreatedAt);
            Assert.Null(rows[1].LastUsedAt);
            Assert.Equal("renewed", rows[1].Reason);
        }

        [Fact]
        public async Task ChangingALiveGrantArchivesItsHistoryAndCreatesANewRow()
        {
            var permission = RbacPermissions.Orders.Read;
            await Service().ApplyAsync(Fresh(_admin), _clerk.Id,
                [new GrantChange(permission, null, GrantState.Allow, null, "original grant")]);
            var oldCreatedAt = DateTime.UtcNow.AddDays(-10);
            var lastUsedAt = DateTime.UtcNow.AddDays(-1);
            using (var db = _database.CreateContext())
                await db.UserPermissionGrant.Where(g => g.UserId == _clerk.Id && g.Permission == permission)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(g => g.CreatedAt, oldCreatedAt)
                        .SetProperty(g => g.LastUsedAt, lastUsedAt));

            await Service().ApplyAsync(Fresh(_admin), _clerk.Id,
                [new GrantChange(permission, null, GrantState.Deny, null, "incident response")]);

            using var verify = _database.CreateContext();
            var rows = verify.UserPermissionGrant.AsNoTracking()
                .Where(g => g.UserId == _clerk.Id && g.Permission == permission)
                .OrderBy(g => g.CreatedAt)
                .ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(EnumGrantEffect.Allow, rows[0].Effect);
            Assert.Equal(_admin.Id, rows[0].GrantedByUserId);
            Assert.Equal("original grant", rows[0].Reason);
            Assert.Equal(oldCreatedAt, rows[0].CreatedAt);
            Assert.Equal(lastUsedAt, rows[0].LastUsedAt);
            Assert.NotNull(rows[0].RevokedAt);
            Assert.Equal(EnumGrantEffect.Deny, rows[1].Effect);
            Assert.Equal("incident response", rows[1].Reason);
            Assert.Null(rows[1].RevokedAt);
            Assert.Null(rows[1].LastUsedAt);
        }

        [Fact]
        public async Task BootstrapAdmins_AppearEffectiveAsHoldersAndInAccessReviews()
        {
            using (var db = _database.CreateContext())
            {
                await db.Enterprise.Where(e => e.Id == _enterprise.Id).ExecuteUpdateAsync(set => set.SetProperty(e => e.IamMode, EnumIamMode.Grants));
                await db.User.Where(u => u.Id == _admin.Id).ExecuteUpdateAsync(set => set.SetProperty(u => u.MfaEnabled, false));
            }
            var iam = Service();
            var access = await iam.GetAccessAsync(_enterprise.Id, _admin.Id);
            var permissions = access.Modules.SelectMany(module => module.Permissions).ToDictionary(p => p.Permission);
            Assert.True(permissions[RbacPermissions.Iam.Read].Effective);
            Assert.True(permissions[RbacPermissions.Iam.Manage].Effective);
            var holder = Assert.Single(await iam.HoldersAsync(_enterprise.Id, RbacPermissions.Iam.Manage));
            Assert.Equal(_admin.Id, holder.UserId);
            Assert.Equal("bootstrap", holder.Source);
            var gap = Assert.Single((await iam.ReviewAsync(_enterprise.Id)).WithoutMfa);
            Assert.Equal(_admin.Id, gap.UserId);
            Assert.Contains(RbacPermissions.Iam.Manage, gap.Permissions);
        }

        [Theory]
        [InlineData("storefront.catalog.read")]
        [InlineData("users.self.read")]
        [InlineData("nothing.such")]
        public async Task ServiceBaselineAndUnknownPermissions_AreNotGrantable(string permission)
        {
            var error = await Refused(() => Grant(_clerk, permission));

            Assert.Equal("not_grantable", error.Code);
        }

        [Fact]
        public async Task NobodyChangesTheirOwnAccess()
        {
            var error = await Refused(() => Grant(_admin, RbacPermissions.Orders.Read));

            Assert.Equal("self_grant", error.Code);
        }

        [Fact]
        public async Task ASensitiveGrant_NeedsAReason()
        {
            var error = await Refused(() => Service().ApplyAsync(Fresh(_admin), _clerk.Id,
                [new GrantChange(RbacPermissions.Bills.Settle, null, GrantState.Allow, null, " ")]));

            Assert.Equal("reason_required", error.Code);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(400)]
        public async Task AnExpiry_MustBeInTheFutureAndWithinAYear(int days)
        {
            var error = await Refused(() => Service().ApplyAsync(Fresh(_admin), _clerk.Id,
                [new GrantChange(RbacPermissions.Orders.Read, null, GrantState.Allow, _time.GetUtcNow().UtcDateTime.AddDays(days), null)]));

            Assert.Equal("invalid_expiry", error.Code);
        }

        [Fact]
        public async Task AfterStartingFromNone_TheAdminCanStillGrantAnything()
        {
            await SwitchToGrants();

            await Grant(_clerk, RbacPermissions.Orders.Read, RbacPermissions.Payroll.Read);

            Assert.True(await Allowed(_clerk, RbacPermissions.Orders.Read));
            Assert.True(await Allowed(_clerk, RbacPermissions.Payroll.Read));
            Assert.False(await Allowed(_admin, RbacPermissions.Payroll.Read));
        }

        [Fact]
        public async Task AnotherEnterprisesUser_IsNotFound()
        {
            var other = new Enterprise { Id = Guid.NewGuid(), Name = "Outra", IsActive = true };
            var stranger = NewUser("Duda", EnumAccessLevel.Employee, enterprise: other.Id);
            await _database.SeedAsync(other, stranger);

            var error = await Refused(() => Grant(stranger, RbacPermissions.Orders.Read));

            Assert.Equal("not_found", error.Code);
            Assert.Equal(404, error.Status);
        }

        [Fact]
        public async Task TheLastManagerWithMfa_CannotBeRemoved()
        {
            var second = NewUser("Bia", EnumAccessLevel.Admin);
            await _database.SeedAsync(second);
            await SwitchToGrants();
            await Grant(second, RbacPermissions.Iam.Read);
            await Service().ApplyAsync(Fresh(_admin), second.Id,
                [new GrantChange(RbacPermissions.Iam.Manage, null, GrantState.Allow, _time.GetUtcNow().UtcDateTime.AddDays(1), "férias da Ana")]);

            var error = await Refused(() => Service().ApplyAsync(Fresh(second), _admin.Id,
                [new GrantChange(RbacPermissions.Iam.Manage, null, GrantState.Inherit, null, null)]));

            Assert.Equal("last_iam_manager", error.Code);
            Assert.True(await Allowed(_admin, RbacPermissions.Iam.Manage));
        }

        [Fact]
        public async Task TheSwitch_NeedsMfaOnTheCaller()
        {
            var admin = NewUser("Bia", EnumAccessLevel.Admin);
            await _database.SeedAsync(admin);

            var error = await Refused(() => Service().SwitchAsync(Fresh(admin), EnumIamMode.Grants));

            Assert.Equal("mfa_required", error.Code);
        }

        [Fact]
        public async Task TheSwitch_RejectsUndefinedModesWithoutChangingTheTenant()
        {
            var error = await Refused(() => Service().SwitchAsync(Fresh(_admin), (EnumIamMode)2));

            Assert.Equal("invalid_iam_mode", error.Code);
            Assert.Equal(EnumIamMode.Role, await Service().ModeAsync(_enterprise.Id));
        }

        [Fact]
        public async Task SwitchingToGrants_ReplacesTemporaryManagerAllowsAndArchivesDenies()
        {
            var temporaryExpiry = _time.GetUtcNow().UtcDateTime.AddHours(2);
            using (var db = _database.CreateContext())
            {
                db.UserPermissionGrant.AddRange(
                    new UserPermissionGrant
                    {
                        Id = Guid.NewGuid(), EnterpriseId = _enterprise.Id, UserId = _admin.Id,
                        Permission = RbacPermissions.Iam.Manage, Effect = EnumGrantEffect.Allow,
                        ExpiresAt = temporaryExpiry, Reason = "temporary", GrantedByUserId = _clerk.Id,
                        CreatedAt = _time.GetUtcNow().UtcDateTime, IsActive = true
                    },
                    new UserPermissionGrant
                    {
                        Id = Guid.NewGuid(), EnterpriseId = _enterprise.Id, UserId = _admin.Id,
                        Permission = RbacPermissions.Iam.Read, Effect = EnumGrantEffect.Deny,
                        Reason = "prior deny", GrantedByUserId = _clerk.Id,
                        CreatedAt = _time.GetUtcNow().UtcDateTime, IsActive = true
                    });
                await db.SaveChangesAsync();
            }

            await SwitchToGrants();

            using var verify = _database.CreateContext();
            var manageRows = await verify.UserPermissionGrant.Where(g => g.UserId == _admin.Id
                && g.Permission == RbacPermissions.Iam.Manage).OrderBy(g => g.CreatedAt).ToListAsync();
            var readRows = await verify.UserPermissionGrant.Where(g => g.UserId == _admin.Id
                && g.Permission == RbacPermissions.Iam.Read).OrderBy(g => g.CreatedAt).ToListAsync();
            Assert.Equal(2, manageRows.Count);
            var archivedManage = Assert.Single(manageRows, g => g.RevokedAt != null);
            var liveManage = Assert.Single(manageRows, g => g.RevokedAt == null);
            Assert.Equal(temporaryExpiry, archivedManage.ExpiresAt);
            Assert.Null(liveManage.ExpiresAt);
            Assert.Equal(EnumGrantEffect.Allow, liveManage.Effect);
            Assert.Equal(2, readRows.Count);
            var archivedRead = Assert.Single(readRows, g => g.RevokedAt != null);
            var liveRead = Assert.Single(readRows, g => g.RevokedAt == null);
            Assert.Equal(EnumGrantEffect.Deny, archivedRead.Effect);
            Assert.Equal(EnumGrantEffect.Allow, liveRead.Effect);
        }

        [Fact]
        public async Task TheSwitch_StartsFromNone_KeepsPreparedGrants_AndLeavesAdminsAbleToManage()
        {
            var prepared = NewUser("Duda", EnumAccessLevel.Employee);
            await _database.SeedAsync(prepared);
            await Grant(prepared, RbacPermissions.Orders.Read);
            Assert.True(await Allowed(_clerk, RbacPermissions.Orders.Read));

            await SwitchToGrants();

            Assert.False(await Allowed(_clerk, RbacPermissions.Orders.Read));
            Assert.True(await Allowed(prepared, RbacPermissions.Orders.Read));
            Assert.False(await Allowed(prepared, RbacPermissions.Orders.Create));
            Assert.True(await Allowed(_admin, RbacPermissions.Iam.Manage));
            Assert.True(await Allowed(_admin, RbacPermissions.Iam.Read));
            Assert.False(await Allowed(_admin, RbacPermissions.Bills.Settle));
            Assert.True(Fresh(_clerk).IamVersion > 0);
        }

        [Fact]
        public async Task AModeChangedOutsideTheService_BindsTheNextRequest()
        {
            Assert.True(await Allowed(_clerk, RbacPermissions.Orders.Read));

            using (var db = _database.CreateContext())
                await db.Enterprise.Where(e => e.Id == _clerk.EnterpriseId).ExecuteUpdateAsync(e => e.SetProperty(x => x.IamMode, EnumIamMode.Grants));

            Assert.False(await Allowed(_clerk, RbacPermissions.Orders.Read));
        }

        [Fact]
        public async Task SwitchingBack_RestoresRoleAccess()
        {
            await SwitchToGrants();
            await Service().SwitchAsync(Fresh(_admin), EnumIamMode.Role);

            Assert.True(await Allowed(_clerk, RbacPermissions.Orders.Read));
        }

        [Fact]
        public async Task ATemplate_WritesTheRolesPermissions_AndReplaceDropsTheRest()
        {
            await Grant(_clerk, RbacPermissions.Bills.Settle);

            await Service().ApplyTemplateAsync(Fresh(_admin), _clerk.Id, EnumAccessLevel.Employee, replace: true, "caixa");

            using var db = _database.CreateContext();
            var live = db.UserPermissionGrant.Where(g => g.UserId == _clerk.Id && g.RevokedAt == null).Select(g => g.Permission).ToList();
            var expected = RbacRolePermissionMap.GetPermissions((short)EnumAccessLevel.Employee).Where(IamCatalog.IsGrantable);
            Assert.Equal(expected.OrderBy(p => p), live.OrderBy(p => p));
        }

        [Fact]
        public async Task ATemplate_WithUndefinedAccessLevel_IsRejectedWithoutChangingGrants()
        {
            await Grant(_clerk, RbacPermissions.Bills.Settle);

            await Assert.ThrowsAsync<IamException>(() => Service().ApplyTemplateAsync(
                Fresh(_admin), _clerk.Id, (EnumAccessLevel)999, replace: true, "invalid template"));

            using var db = _database.CreateContext();
            Assert.Contains(db.UserPermissionGrant, g => g.UserId == _clerk.Id
                && g.Permission == RbacPermissions.Bills.Settle && g.RevokedAt == null);
        }

        [Fact]
        public async Task Grants_WithUnknownState_IsRejectedWithoutRevokingExistingGrant()
        {
            await Grant(_clerk, RbacPermissions.Bills.Settle);

            var result = await ControllerFor(_admin).Grants(_clerk.Id, new UpdateGrantsRequest
            {
                Changes = [new GrantChangeRequest { Permission = RbacPermissions.Bills.Settle, State = "alow" }]
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("invalid_grant_state", badRequest.Value!.GetType().GetProperty("code")!.GetValue(badRequest.Value));
            using var db = _database.CreateContext();
            Assert.Contains(db.UserPermissionGrant, g => g.UserId == _clerk.Id
                && g.Permission == RbacPermissions.Bills.Settle && g.RevokedAt == null);
        }

        [Fact]
        public async Task ResetMfa_ForTheOnlyGrantsModeManager_IsRejectedAndRolledBack()
        {
            using (var db = _database.CreateContext())
            {
                db.Enterprise.Single(e => e.Id == _enterprise.Id).IamMode = EnumIamMode.Grants;
                db.User.Single(u => u.Id == _clerk.Id).MfaEnabled = true;
                db.UserPermissionGrant.Add(new UserPermissionGrant
                {
                    Id = Guid.NewGuid(), EnterpriseId = _enterprise.Id, UserId = _clerk.Id,
                    Permission = RbacPermissions.Iam.Manage, Effect = EnumGrantEffect.Allow,
                    GrantedByUserId = _admin.Id, CreatedAt = _time.GetUtcNow().UtcDateTime, IsActive = true
                });
                await db.SaveChangesAsync();
            }

            using var context = _database.CreateContext();
            var users = new Repository<User>(context);
            var iam = new IamService(context, new AuditTrailWriter(context), _cache, new Repository<Enterprise>(context), _time);
            var mfa = new MfaService(users, new Moq.Mock<Shared.Repositories.IRepository<MfaRecoveryCode>>().Object,
                new Moq.Mock<Shared.Repositories.IRepository<RefreshToken>>().Object, new AuditTrailWriter(context), _cache, _time, iam);

            var error = await Assert.ThrowsAsync<IamException>(() => mfa.ResetAsync(_clerk.Id, _admin.Id));

            Assert.Equal("last_iam_manager", error.Code);
            using var verify = _database.CreateContext();
            Assert.True(verify.User.Single(u => u.Id == _clerk.Id).MfaEnabled);
        }

        [Fact]
        public async Task ResetMfa_ForTheBootstrapAdmin_IsRejectedAndRolledBack()
        {
            using (var db = _database.CreateContext())
            {
                db.Enterprise.Single(e => e.Id == _enterprise.Id).IamMode = EnumIamMode.Grants;
                await db.SaveChangesAsync();
            }

            using var context = _database.CreateContext();
            var users = new Repository<User>(context);
            var iam = new IamService(context, new AuditTrailWriter(context), _cache, new Repository<Enterprise>(context), _time);
            var mfa = new MfaService(users, new Moq.Mock<Shared.Repositories.IRepository<MfaRecoveryCode>>().Object,
                new Moq.Mock<Shared.Repositories.IRepository<RefreshToken>>().Object, new AuditTrailWriter(context), _cache, _time, iam);

            var error = await Assert.ThrowsAsync<IamException>(() => mfa.ResetAsync(_admin.Id, _admin.Id));

            Assert.Equal("last_iam_manager", error.Code);
            using var verify = _database.CreateContext();
            Assert.True(verify.User.Single(u => u.Id == _admin.Id).MfaEnabled);
        }

        [Fact]
        public async Task ResetMfa_InRoleModeRequiresAnotherMfaEnabledAdminToSurvive()
        {
            var adminWithoutMfa = NewUser("Bia", EnumAccessLevel.Admin, mfa: false);
            await _database.SeedAsync(adminWithoutMfa);

            using var context = _database.CreateContext();
            var users = new Repository<User>(context);
            var iam = new IamService(context, new AuditTrailWriter(context), _cache, new Repository<Enterprise>(context), _time);
            var mfa = new MfaService(users, new Moq.Mock<Shared.Repositories.IRepository<MfaRecoveryCode>>().Object,
                new Moq.Mock<Shared.Repositories.IRepository<RefreshToken>>().Object, new AuditTrailWriter(context), _cache, _time, iam);

            var error = await Assert.ThrowsAsync<IamException>(() => mfa.ResetAsync(_admin.Id, _admin.Id));

            Assert.Equal("last_iam_manager", error.Code);
            using var verify = _database.CreateContext();
            Assert.True(verify.User.Single(u => u.Id == _admin.Id).MfaEnabled);
        }

        [Fact]
        public async Task EveryGrantChange_IsAudited()
        {
            await Grant(_clerk, RbacPermissions.Orders.Read, RbacPermissions.Orders.Create);

            using var db = _database.CreateContext();
            var row = Assert.Single(db.AuditTrails.Where(a => a.EntityName == AuditedEntities.Iam));
            Assert.Equal(_admin.Id, row.UserId);
            Assert.Equal(_clerk.Id, row.EntityId);
            Assert.Contains(RbacPermissions.Orders.Create, row.ChangesJson);
        }

        [Fact]
        public async Task TheTokenScopesStillBind_InGrantsMode()
        {
            await SwitchToGrants();
            await Grant(_clerk, RbacPermissions.Pdv.Operate, RbacPermissions.Orders.Read);

            Assert.True(await Allowed(_clerk, RbacPermissions.Pdv.Operate, PdvTokenScope.ClaimValue));
            Assert.False(await Allowed(_clerk, RbacPermissions.Orders.Read, PdvTokenScope.ClaimValue));
            Assert.False(await Allowed(_clerk, RbacPermissions.Storefront.CatalogRead));
            Assert.True(await Allowed(_clerk, RbacPermissions.Storefront.CatalogRead, StorefrontTokenScope.ClaimValue));
            Assert.False(await Allowed(_clerk, RbacPermissions.Orders.Read, StorefrontTokenScope.ClaimValue));
        }

        [Fact]
        public async Task TheTillOperatorList_ReadsGrantsInGrantsMode()
        {
            await SwitchToGrants();
            await Grant(_clerk, RbacPermissions.Pdv.Operate);

            var granted = await Evaluator().GrantedAsync([Fresh(_clerk), Fresh(_admin)]);

            Assert.Equal([RbacPermissions.Pdv.Operate], PdvTokenScope.PermissionsIn(granted[_clerk.Id]));
            Assert.Empty(PdvTokenScope.PermissionsIn(granted[_admin.Id]));
        }
    }
}
