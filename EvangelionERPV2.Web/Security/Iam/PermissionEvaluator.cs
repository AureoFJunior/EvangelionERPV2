using System.Security.Claims;
using System.Text.Json;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace EvangelionERPV2.Web.Security.Iam
{
    /// <summary>
    /// Role mode: <see cref="RolePermissionEvaluator"/>. Grants mode (zero trust): the baseline plus the
    /// user's live grants, narrowed by the token's scope (SPEC-iam-zero-trust §3.3). The Limen service
    /// set is decided by the token alone in both modes, because no human can hold it. In both modes a
    /// sensitive or privileged permission then needs MFA and a fresh step-up on this token (§6).
    /// </summary>
    public sealed class PermissionEvaluator : IPermissionEvaluator
    {
        private static readonly TimeSpan MaxGrantCacheAge = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan BootstrapAuditInterval = TimeSpan.FromHours(1);
        private readonly Dictionary<Guid, EnumIamMode> _modes = new();

        private readonly AppDbContext _db;
        private readonly IDistributedCache _cache;
        private readonly TimeProvider _time;
        private readonly bool _enforceStepUp;
        private readonly IDistributedLease? _useTracking;
        private readonly IServiceScopeFactory? _scopes;
        private static readonly TimeSpan UseTrackingInterval = TimeSpan.FromHours(1);

        /// <summary>
        /// In front of Redis, which is remote: every permission check paid two or three round-trips to it
        /// (~145 ms each), so every request ~300 ms. Held sets are keyed by the IAM version and mode read
        /// fresh from the database on each request, so a change still binds the very next request.
        /// </summary>
        // ponytail: one process-wide cache; each API instance holds its own copy for at most LocalHeldAge.
        private static readonly MemoryCache Local = new(new MemoryCacheOptions { SizeLimit = 50_000 });
        private static readonly TimeSpan LocalHeldAge = TimeSpan.FromSeconds(30);

        /// <param name="configuration">
        /// <c>Iam:EnforceStepUp</c> (default true) turns the MFA step-up gate on; off only until the client
        /// can prompt for the code, since every sensitive action answers <c>step_up_required</c> otherwise.
        /// </param>
        public PermissionEvaluator(AppDbContext db, IDistributedCache cache, TimeProvider? time = null, IConfiguration? configuration = null,
            IDistributedLease? useTracking = null, IServiceScopeFactory? scopes = null)
        {
            _useTracking = useTracking;
            _scopes = scopes;
            _db = db;
            _cache = cache;
            _time = time ?? TimeProvider.System;
            _enforceStepUp = configuration?.GetValue("Iam:EnforceStepUp", true) ?? true;
        }

        private bool Fresh(DateTimeOffset? verifiedAt, PermissionTier tier) =>
            verifiedAt is { } at && at >= _time.GetUtcNow() - StepUp.Window(tier) && at <= _time.GetUtcNow() + TimeSpan.FromMinutes(1);

        public static string HeldKey(Guid userId, int version, EnumIamMode mode) => $"iam:held:{userId:N}:{version}:{(byte)mode}";

        /// <summary>
        /// The rule both modes share: Role mode starts from the user's role, Grants mode from nothing; then
        /// the user's own allows add and denies remove; the baseline is always held.
        /// </summary>
        public static HashSet<string> Compose(EnumIamMode mode, short accessLevel, IEnumerable<(string Permission, EnumGrantEffect Effect)> grants)
        {
            var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mode != EnumIamMode.Grants)
                held.UnionWith(RbacRolePermissionMap.GetPermissions(accessLevel));

            var own = grants.Where(g => IamCatalog.IsGrantable(g.Permission)).ToList();
            held.UnionWith(own.Where(g => g.Effect == EnumGrantEffect.Allow).Select(g => g.Permission));
            held.ExceptWith(own.Where(g => g.Effect == EnumGrantEffect.Deny).Select(g => g.Permission));
            held.UnionWith(IamCatalog.Baseline);
            return held;
        }

        public async Task<PermissionDecision> DecideAsync(ClaimsPrincipal principal, User user, string permission)
        {
            if (StepUp.IsMachineClient(principal))
                return PermissionDecision.Denied;

            if (StorefrontTokenScope.ServicePermissions.Contains(permission))
                return StorefrontTokenScope.IsStorefrontScoped(principal) ? PermissionDecision.Allowed : PermissionDecision.Denied;

            var holds = StorefrontTokenScope.AllowsThroughToken(principal, permission)
                        && (await HeldAsync(user)).Contains(permission);
            if (!holds)
                return PermissionDecision.Denied;

            var decision = PermissionDecision.Allowed;
            var tier = IamCatalog.TierOf(permission);
            if (_enforceStepUp && tier != PermissionTier.Standard && !StepUp.IsMachineClient(principal))
                decision = !user.MfaEnabled ? PermissionDecision.MfaRequired
                    : Fresh(await StepUp.VerifiedAtAsync(_cache, principal), tier) ? PermissionDecision.Allowed
                    : PermissionDecision.StepUpRequired;

            if (decision == PermissionDecision.Allowed)
                await TrackUseAsync(user, permission);
            return decision;
        }

        /// <summary>Stamps <c>LastUsedAt</c> at most once per grant per hour, for the access review. Never fails a check.</summary>
        private async Task TrackUseAsync(User user, string permission)
        {
            if (_useTracking == null || IamCatalog.Baseline.Contains(permission))
                return;

            var usedKey = $"iam:used:{user.Id:N}:{permission}";
            if (Local.TryGetValue(usedKey, out _))
                return;
            Local.Set(usedKey, true, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = UseTrackingInterval, Size = 1 });

            try
            {
                if (await _useTracking.TryAcquireAsync(usedKey, UseTrackingInterval) == null)
                    return;

                var now = _time.GetUtcNow().UtcDateTime;
                await _db.UserPermissionGrant.IgnoreQueryFilters()
                    .Where(g => g.UserId == user.Id && g.Permission == permission && g.RevokedAt == null && g.Effect == EnumGrantEffect.Allow)
                    .ExecuteUpdateAsync(set => set.SetProperty(g => g.LastUsedAt, now));
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("IAM grant use could not be recorded. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }

        public async Task<IReadOnlySet<string>> EffectiveAsync(ClaimsPrincipal principal, User user)
        {
            var holds = await HoldsAsync(principal, user);
            if (!_enforceStepUp || StepUp.IsMachineClient(principal))
                return holds;

            var verifiedAt = user.MfaEnabled ? await StepUp.VerifiedAtAsync(_cache, principal) : null;
            return holds
                .Where(p => StorefrontTokenScope.ServicePermissions.Contains(p)
                            || IamCatalog.TierOf(p) is var tier && (tier == PermissionTier.Standard || (user.MfaEnabled && Fresh(verifiedAt, tier))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public async Task<IReadOnlySet<string>> HoldsAsync(ClaimsPrincipal principal, User user)
        {
            if (StepUp.IsMachineClient(principal))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var effective = (await HeldAsync(user))
                .Where(permission => StorefrontTokenScope.AllowsThroughToken(principal, permission))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (StorefrontTokenScope.IsStorefrontScoped(principal))
                effective.UnionWith(StorefrontTokenScope.ServicePermissions);
            return effective;
        }

        public async Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> GrantedAsync(IReadOnlyCollection<User> users)
        {
            var result = new Dictionary<Guid, IReadOnlySet<string>>();
            foreach (var group in users.DistinctBy(u => u.Id).GroupBy(u => u.EnterpriseId))
            {
                var members = group.ToList();
                var mode = await ModeAsync(group.Key);
                var now = _time.GetUtcNow().UtcDateTime;
                var ids = members.Select(u => u.Id).ToList();
                var grants = await ActiveGrants(group.Key, now).Where(g => ids.Contains(g.UserId))
                    .Select(g => new { g.UserId, g.Permission, g.Effect })
                    .ToListAsync();
                var byUser = grants.ToLookup(g => g.UserId, g => (g.Permission, g.Effect));
                foreach (var member in members)
                    result[member.Id] = Compose(mode, member.AccessLevel, byUser[member.Id]);
            }

            return result;
        }

        /// <summary>Baseline plus live grants; cached per <see cref="User.IamVersion"/> and never past the earliest expiry.</summary>
        public async Task<IReadOnlySet<string>> HeldAsync(User user)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var mode = await ModeAsync(user.EnterpriseId);
            var key = HeldKey(user.Id, user.IamVersion, mode);
            if (Local.TryGetValue(key, out CachedHeld? local) && local is not null && (local.ValidUntil == null || local.ValidUntil > now))
                return local.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            try
            {
                var raw = await _cache.GetStringAsync(key);
                if (raw != null && JsonSerializer.Deserialize<CachedHeld>(raw) is { } cached && (cached.ValidUntil == null || cached.ValidUntil > now))
                {
                    RememberLocally(key, cached, now);
                    return cached.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("IAM grant cache unreadable, reading the database. ErrorType={ErrorType}", ex.GetType().Name);
            }

            var grants = await ActiveGrants(user.EnterpriseId, now).Where(g => g.UserId == user.Id)
                .Select(g => new { g.Permission, g.Effect, g.ExpiresAt })
                .ToListAsync();
            var held = Compose(mode, user.AccessLevel, grants.Select(g => (g.Permission, g.Effect)));
            if (mode == EnumIamMode.Grants && user.AccessLevel == (short)EnumAccessLevel.Admin && user.IsActive == true && !await HasManagerAsync(user.EnterpriseId, now))
            {
                Log.Logger.Warning("IAM bootstrap: enterprise has no access manager, so its admin holds iam.* until one is granted. UserId={UserId}", user.Id);
                held.UnionWith([RbacPermissions.Iam.Read, RbacPermissions.Iam.Manage]);
                await AuditBootstrapAsync(user);
                return held;
            }
            var earliest = grants.Where(g => g.ExpiresAt != null).Select(g => g.ExpiresAt).Min();

            var ttl = earliest is { } until ? until - now : MaxGrantCacheAge;
            if (ttl > MaxGrantCacheAge)
                ttl = MaxGrantCacheAge;
            if (ttl > TimeSpan.FromSeconds(1))
            {
                RememberLocally(key, new CachedHeld([.. held], earliest), now);
                try
                {
                    await _cache.SetStringAsync(key, JsonSerializer.Serialize(new CachedHeld([.. held], earliest)),
                        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
                }
                catch (Exception ex)
                {
                    Log.Logger.Warning("IAM grant cache unwritable. ErrorType={ErrorType}", ex.GetType().Name);
                }
            }

            return held;
        }

        /// <summary>
        /// Read from the database once per request (this evaluator is scoped), never from a shared cache: a
        /// switch to zero trust must bind the very next request, even if a cache could not be cleared.
        /// </summary>
        public async Task<EnumIamMode> ModeAsync(Guid? enterpriseId)
        {
            if (enterpriseId is not { } id || id == Guid.Empty)
                return EnumIamMode.Role;

            if (_modes.TryGetValue(id, out var known))
                return known;

            var mode = await _db.Enterprise.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.Id == id)
                .Select(e => e.IamMode)
                .FirstOrDefaultAsync();
            _modes[id] = mode;
            return mode;
        }

        /// <summary>
        /// An admin holding iam.* by the bootstrap rule is audited, at most once per user per hour, through its
        /// own scope so the audit row can never flush the request's unsaved changes. Never fails a check.
        /// </summary>
        private async Task AuditBootstrapAsync(User user)
        {
            if (_scopes == null || _useTracking == null)
                return;

            try
            {
                if (await _useTracking.TryAcquireAsync($"iam:bootstrap:{user.Id:N}", BootstrapAuditInterval) == null)
                    return;

                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.AuditTrails.Add(new AuditTrail
                {
                    UserId = user.Id,
                    EnterpriseId = user.EnterpriseId,
                    ChangedAt = _time.GetUtcNow().UtcDateTime,
                    Action = "EDIT",
                    EntityName = Shared.Auditing.AuditedEntities.Iam,
                    EntityId = user.Id,
                    ChangesJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["Bootstrap"] = new { OldValue = (string?)null, NewValue = "iam.read, iam.manage held by the admin role: no access manager in the enterprise" }
                    })
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("IAM bootstrap could not be audited. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }

        /// <summary>
        /// Whether anyone in the enterprise holds iam.manage. A new zero-trust enterprise has nobody until
        /// its admin grants it, so the admin role bootstraps it; the last-manager guard keeps the API from
        /// ever returning an established enterprise to that state.
        /// </summary>
        private Task<bool> HasManagerAsync(Guid? enterpriseId, DateTime now) =>
            ActiveGrants(enterpriseId, now).AnyAsync(g => g.Permission == RbacPermissions.Iam.Manage && g.Effect == EnumGrantEffect.Allow);

        private IQueryable<UserPermissionGrant> ActiveGrants(Guid? enterpriseId, DateTime now) =>
            _db.UserPermissionGrant.IgnoreQueryFilters().AsNoTracking()
                .Where(g => g.EnterpriseId == enterpriseId && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now));


        private sealed record CachedHeld(string[] Permissions, DateTime? ValidUntil);

        private static void RememberLocally(string key, CachedHeld held, DateTime now)
        {
            var age = held.ValidUntil is { } until && until - now < LocalHeldAge ? until - now : LocalHeldAge;
            if (age > TimeSpan.Zero)
                Local.Set(key, held, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = age, Size = 1 });
        }
    }
}
