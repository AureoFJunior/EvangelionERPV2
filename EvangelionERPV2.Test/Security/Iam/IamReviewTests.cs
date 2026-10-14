using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Web.Security;
using EvangelionERPV2.Web.Security.Iam;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace EvangelionERPV2.Test.Security.Iam
{
    // SPEC-iam-zero-trust IAM-3: knowing which grants are used, which are stale, and who lacks MFA.
    public class IamReviewTests : IDisposable
    {
        private readonly IamTestDatabase _database = new();
        private readonly MemoryDistributedCache _cache = new(Options.Create(new MemoryDistributedCacheOptions()));
        private readonly MovableTime _time = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        private readonly OnceLease _lease = new();
        private readonly Enterprise _enterprise = new() { Id = Guid.NewGuid(), Name = "Loja", IsActive = true };
        private readonly User _admin;
        private readonly User _clerk;

        public IamReviewTests()
        {
            _admin = NewUser("Ana", EnumAccessLevel.Admin, mfa: true);
            _clerk = NewUser("Caio", EnumAccessLevel.Employee);
            _database.SeedAsync(_enterprise, _admin, _clerk).GetAwaiter().GetResult();
        }

        public void Dispose() => _database.Dispose();

        private User NewUser(string name, EnumAccessLevel level, bool mfa = false) => new()
        {
            Id = Guid.NewGuid(), FirstName = name, LastName = "Silva", UserName = name.ToLowerInvariant(), Email = $"{name}@loja.com", Password = "x",
            AccessLevel = (short)level, EnterpriseId = _enterprise.Id, IsActive = true, MfaEnabled = mfa
        };

        private IamService Service()
        {
            var db = _database.CreateContext();
            return new IamService(db, new AuditTrailWriter(db), _cache, new Repository<Enterprise>(db), _time);
        }

        private User Fresh(User user)
        {
            using var db = _database.CreateContext();
            return db.User.AsNoTracking().Single(u => u.Id == user.Id);
        }

        private static ClaimsPrincipal Token(User user) =>
            new(new ClaimsIdentity([new Claim(ClaimTypes.Sid, user.Id.ToString()), new Claim(ClaimTypes.GroupSid, user.EnterpriseId.ToString()!)], "test"));

        private UserPermissionGrant GrantRow(string permission)
        {
            using var db = _database.CreateContext();
            return db.UserPermissionGrant.AsNoTracking().Single(g => g.UserId == _clerk.Id && g.Permission == permission);
        }

        [Fact]
        public async Task AGrantThatAuthorises_IsStampedAtMostOncePerHour()
        {
            await Service().SwitchAsync(Fresh(_admin), EnumIamMode.Grants);
            await Service().ApplyAsync(Fresh(_admin), _clerk.Id, [new GrantChange(RbacPermissions.Orders.Read, null, GrantState.Allow, null, null)]);
            var evaluator = new PermissionEvaluator(_database.CreateContext(), _cache, _time, useTracking: _lease);

            Assert.Equal(PermissionDecision.Allowed, await evaluator.DecideAsync(Token(_clerk), Fresh(_clerk), RbacPermissions.Orders.Read));
            Assert.Equal(_time.GetUtcNow().UtcDateTime, GrantRow(RbacPermissions.Orders.Read).LastUsedAt);

            _time.Advance(TimeSpan.FromMinutes(10));
            await evaluator.DecideAsync(Token(_clerk), Fresh(_clerk), RbacPermissions.Orders.Read);
            Assert.Equal(_time.GetUtcNow().UtcDateTime.AddMinutes(-10), GrantRow(RbacPermissions.Orders.Read).LastUsedAt);
            Assert.Single(_lease.Taken);
        }

        [Fact]
        public async Task AnAdminHoldingIamByBootstrap_IsAuditedAtMostOncePerHour()
        {
            using (var start = _database.CreateContext())
                await start.Enterprise.Where(e => e.Id == _enterprise.Id).ExecuteUpdateAsync(e => e.SetProperty(x => x.IamMode, EnumIamMode.Grants));
            var scopes = new ServiceCollection().AddScoped(_ => _database.CreateContext()).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

            for (var i = 0; i < 2; i++)
            {
                var evaluator = new PermissionEvaluator(_database.CreateContext(), _cache, _time, useTracking: _lease, scopes: scopes);
                Assert.Contains(RbacPermissions.Iam.Manage, await evaluator.HoldsAsync(Token(_admin), Fresh(_admin)));
            }

            using var db = _database.CreateContext();
            var row = Assert.Single(db.AuditTrails.AsNoTracking().Where(a => a.EntityName == AuditedEntities.Iam && a.ChangesJson.Contains("Bootstrap")));
            Assert.Equal(_admin.Id, row.EntityId);
            Assert.Equal(_admin.Id, row.UserId);
        }

        [Fact]
        public async Task ABaselinePermission_IsNotTracked()
        {
            await Service().SwitchAsync(Fresh(_admin), EnumIamMode.Grants);
            var evaluator = new PermissionEvaluator(_database.CreateContext(), _cache, _time, useTracking: _lease);

            await evaluator.DecideAsync(Token(_clerk), Fresh(_clerk), RbacPermissions.Users.ReadSelf);

            Assert.Empty(_lease.Taken);
        }

        [Fact]
        public async Task TheReview_BucketsStaleGrants_ListsUnknownOnes_AndFindsHoldersWithoutMfa()
        {
            await Service().SwitchAsync(Fresh(_admin), EnumIamMode.Grants);
            await Service().ApplyAsync(Fresh(_admin), _clerk.Id,
            [
                new GrantChange(RbacPermissions.Orders.Read, null, GrantState.Allow, null, null),
                new GrantChange(RbacPermissions.Orders.Create, null, GrantState.Allow, null, null),
                new GrantChange(RbacPermissions.Bills.Settle, null, GrantState.Allow, null, "fechamento")
            ]);
            using (var db = _database.CreateContext())
            {
                var now = _time.GetUtcNow().UtcDateTime;
                await db.UserPermissionGrant.Where(g => g.Permission == RbacPermissions.Orders.Read)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.LastUsedAt, now.AddDays(-45)));
                await db.UserPermissionGrant.Where(g => g.Permission == RbacPermissions.Orders.Create)
                    .ExecuteUpdateAsync(s => s.SetProperty(g => g.CreatedAt, now.AddDays(-120)));
                db.UserPermissionGrant.Add(new UserPermissionGrant
                {
                    Id = Guid.NewGuid(), EnterpriseId = _enterprise.Id, UserId = _clerk.Id, Permission = "legacy.removed",
                    GrantedByUserId = _admin.Id, CreatedAt = now, IsActive = true
                });
                await db.SaveChangesAsync();
            }

            var review = await Service().ReviewAsync(_enterprise.Id);

            Assert.Equal([(RbacPermissions.Orders.Create, 90), (RbacPermissions.Orders.Read, 30)],
                review.Unused.Where(u => u.UserId == _clerk.Id).Select(u => (u.Permission, u.Bucket)));
            Assert.Equal("legacy.removed", Assert.Single(review.Unknown).Permission);
            var gap = Assert.Single(review.WithoutMfa);
            Assert.Equal(_clerk.Id, gap.UserId);
            Assert.Equal([RbacPermissions.Bills.Settle], gap.Permissions);
        }

        [Fact]
        public async Task InRoleMode_TheMfaGapComesFromTheRole()
        {
            var manager = NewUser("Bia", EnumAccessLevel.Manager);
            await _database.SeedAsync(manager);

            var review = await Service().ReviewAsync(_enterprise.Id);

            var gap = Assert.Single(review.WithoutMfa);
            Assert.Equal(manager.Id, gap.UserId);
            Assert.Contains(RbacPermissions.Accounting.Export, gap.Permissions);
            Assert.DoesNotContain(gap.Permissions, p => p.StartsWith("pdv.", StringComparison.Ordinal));
        }

        private sealed class OnceLease : IDistributedLease
        {
            public HashSet<string> Taken { get; } = [];

            public Task<string?> TryAcquireAsync(string key, TimeSpan timeToLive) =>
                Task.FromResult(Taken.Add(key) ? Guid.NewGuid().ToString("N") : null);

            public Task ReleaseAsync(string key, string token) => Task.CompletedTask;
        }
    }
}
