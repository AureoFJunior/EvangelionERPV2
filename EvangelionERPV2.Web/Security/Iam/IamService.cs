using System.Text.Json;
using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.UserModule.Application.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace EvangelionERPV2.Web.Security.Iam
{
    public sealed class IamException(string code, string message, int status = StatusCodes.Status400BadRequest) : Exception(message)
    {
        public string Code { get; } = code;
        public int Status { get; } = status;
    }

    /// <summary>Inherit: follow the role (Role mode) or hold nothing (Grants mode). Allow adds; Deny takes away what the role gives.</summary>
    public enum GrantState { Inherit, Allow, Deny }

    public sealed record GrantChange(string? Permission, string? Module, GrantState State, DateTime? ExpiresAt, string? Reason);

    /// <summary>
    /// Writes and reads per-user grants (SPEC-iam-zero-trust §7). Every write for one enterprise runs
    /// under that enterprise's row lock, so the last-manager guard cannot be raced past.
    /// </summary>
    public sealed class IamService : IMfaDisableGuard
    {
        private const int MaxReasonLength = 280;
        private static readonly TimeSpan MaxGrantLifetime = TimeSpan.FromDays(366);
        private const string SwitchReason = "Conversão para IAM";

        private readonly AppDbContext _db;
        private readonly IAuditTrailWriter _audit;
        private readonly IDistributedCache _cache;
        private readonly IRepository<Enterprise> _enterprises;
        private readonly TimeProvider _time;

        public IamService(AppDbContext db, IAuditTrailWriter audit, IDistributedCache cache,
            IRepository<Enterprise> enterprises, TimeProvider? time = null)
        {
            _db = db;
            _audit = audit;
            _cache = cache;
            _enterprises = enterprises;
            _time = time ?? TimeProvider.System;
        }

        private DateTime Now => _time.GetUtcNow().UtcDateTime;

        public async Task<UserAccessDTO> GetAccessAsync(Guid enterpriseId, Guid userId)
        {
            var user = await _db.User.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.EnterpriseId == enterpriseId && u.IsActive == true)
                ?? throw NotFound();
            var mode = await ModeAsync(enterpriseId);
            var now = Now;
            var grants = await _db.UserPermissionGrant.AsNoTracking()
                .Where(g => g.UserId == userId && g.EnterpriseId == enterpriseId && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now))
                .ToListAsync();
            var byPermission = grants.ToDictionary(g => g.Permission, StringComparer.OrdinalIgnoreCase);
            var role = RbacRolePermissionMap.GetPermissions(user.AccessLevel).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var effective = PermissionEvaluator.Compose(mode, user.AccessLevel, grants.Select(g => (g.Permission, g.Effect)));
            if (await IsBootstrapAdminAsync(enterpriseId, mode, user.AccessLevel, now))
                effective.UnionWith([RbacPermissions.Iam.Read, RbacPermissions.Iam.Manage]);

            return new UserAccessDTO(
                user.Id,
                $"{user.FirstName} {user.LastName}".Trim(),
                mode,
                user.MfaEnabled,
                user.AccessLevel,
                IamCatalog.Modules.Select(module => new AccessModuleDTO(
                    module.Key,
                    module.Label,
                    module.Permissions.Select(p =>
                    {
                        byPermission.TryGetValue(p.Permission, out var grant);
                        return new AccessPermissionDTO(
                            p.Permission, p.Label, TierName(p.Tier), p.Grantable, p.Baseline,
                            Override: grant == null ? null : grant.Effect == EnumGrantEffect.Deny ? "deny" : "allow",
                            Effective: effective.Contains(p.Permission),
                            grant?.ExpiresAt, grant?.GrantedByUserId, grant?.Reason, grant?.LastUsedAt,
                            HeldByRole: role.Contains(p.Permission));
                    }).ToList())).ToList());
        }

        public async Task<UserAccessDTO> ApplyAsync(User caller, Guid targetId, IReadOnlyList<GrantChange> changes)
        {
            var enterpriseId = caller.EnterpriseId ?? throw NotFound();
            if (targetId == caller.Id)
                throw new IamException("self_grant", "Você não pode alterar os próprios acessos.");

            var wanted = Validate(changes);

            await _enterprises.ExecuteInTransactionAsync(async () =>
            {
                await _enterprises.LockForUpdateAsync(enterpriseId);
                var target = await _db.User.FirstOrDefaultAsync(u => u.Id == targetId && u.EnterpriseId == enterpriseId && u.IsActive == true)
                    ?? throw NotFound();

                if (await WriteAsync(caller, target, wanted))
                    await EnsureManagerRemainsAsync(enterpriseId, await ModeAsync(enterpriseId));
            });

            return await GetAccessAsync(enterpriseId, targetId);
        }

        /// <summary>Deactivates an access manager atomically with the last-manager invariant.</summary>
        public Task<User> DeactivateUserAsync(Guid enterpriseId, Guid userId) =>
            _enterprises.ExecuteInTransactionAsync(async () =>
            {
                await _enterprises.LockForUpdateAsync(enterpriseId);
                var user = await _db.User.FirstOrDefaultAsync(u => u.Id == userId && u.EnterpriseId == enterpriseId && u.IsActive == true)
                    ?? throw NotFound();

                user.IsActive = false;
                user.UpdatedAt = Now;
                user.IamVersion++;
                await _db.SaveChangesAsync();
                await EnsureManagerRemainsAsync(enterpriseId, await ModeAsync(enterpriseId));
                return user;
            });

        /// <summary>Must be called inside the MFA transaction before locking the user row.</summary>
        public Task LockEnterpriseForMfaChangeAsync(Guid enterpriseId) => _enterprises.LockForUpdateAsync(enterpriseId);

        /// <summary>Checks the last-manager invariant while the caller holds the enterprise lock.</summary>
        public async Task EnsureMfaCanBeDisabledAsync(Guid enterpriseId, Guid userId)
        {
            var mode = await ModeAsync(enterpriseId);
            var now = Now;
            var target = await _db.User.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId
                && u.EnterpriseId == enterpriseId && u.IsActive == true);
            if (target == null)
                return;

            var activeManageGrant = await _db.UserPermissionGrant.AnyAsync(g => g.EnterpriseId == enterpriseId
                && g.Permission == RbacPermissions.Iam.Manage && g.Effect == EnumGrantEffect.Allow
                && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now));
            var targetManageGrant = await _db.UserPermissionGrant.AnyAsync(g => g.EnterpriseId == enterpriseId && g.UserId == userId
                && g.Permission == RbacPermissions.Iam.Manage && g.Effect == EnumGrantEffect.Allow
                && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now));
            var isManager = mode == EnumIamMode.Grants
                ? target.MfaEnabled && (targetManageGrant || (target.AccessLevel == (short)EnumAccessLevel.Admin && !activeManageGrant))
                : targetManageGrant || (target.AccessLevel == (short)EnumAccessLevel.Admin
                    && !_db.UserPermissionGrant.Any(g => g.EnterpriseId == enterpriseId && g.UserId == userId
                        && g.Permission == RbacPermissions.Iam.Manage && g.Effect == EnumGrantEffect.Deny
                        && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now)));

            if (isManager)
                await EnsureManagerRemainsAsync(enterpriseId, mode, userId);
        }

        public Task<UserAccessDTO> ApplyTemplateAsync(User caller, Guid targetId, EnumAccessLevel template, bool replace, string? reason)
        {
            if (!Enum.IsDefined(template))
                throw new IamException("invalid_access_template", "O modelo de acesso informado é inválido.");

            var permissions = RbacRolePermissionMap.GetPermissions((short)template)
                .Where(IamCatalog.IsGrantable)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var changes = permissions.Select(p => new GrantChange(p, null, GrantState.Allow, null, reason)).ToList();
            if (replace)
                changes.AddRange(IamCatalog.Permissions
                    .Where(p => p.Grantable && !permissions.Contains(p.Permission))
                    .Select(p => new GrantChange(p.Permission, null, GrantState.Inherit, null, reason)));

            return ApplyAsync(caller, targetId, changes);
        }

        /// <summary>Moves the enterprise between Role and Grants mode. Starting zero trust keeps no role grants (owner decision 1).</summary>
        public async Task<EnumIamMode> SwitchAsync(User caller, EnumIamMode mode)
        {
            var enterpriseId = caller.EnterpriseId ?? throw NotFound();
            if (!Enum.IsDefined(mode))
                throw new IamException("invalid_iam_mode", "O modo de acesso informado é inválido.");
            if (!caller.MfaEnabled)
                throw new IamException("mfa_required", "Ative a verificação em duas etapas antes de trocar o modo de acesso.", StatusCodes.Status409Conflict);

            await _enterprises.ExecuteInTransactionAsync(async () =>
            {
                await _enterprises.LockForUpdateAsync(enterpriseId);
                var enterprise = await _db.Enterprise.FirstAsync(e => e.Id == enterpriseId);
                if (enterprise.IamMode == mode)
                    return;

                var now = Now;
                if (mode == EnumIamMode.Grants)
                {
                    var admins = await _db.User
                        .Where(u => u.EnterpriseId == enterpriseId && u.IsActive == true && u.AccessLevel == (short)EnumAccessLevel.Admin)
                        .ToListAsync();
                    var adminIds = admins.Select(a => a.Id).ToList();
                    var withGrants = (await _db.UserPermissionGrant
                        .Where(g => adminIds.Contains(g.UserId) && g.RevokedAt == null && g.Effect == EnumGrantEffect.Allow)
                        .Select(g => g.UserId)
                        .Distinct()
                        .ToListAsync()).ToHashSet();

                    foreach (var admin in admins.Where(a => a.Id == caller.Id || !withGrants.Contains(a.Id)))
                        await EnsureIamGrantsAsync(enterpriseId, admin.Id, caller.Id, now);
                }

                var before = enterprise.IamMode;
                enterprise.IamMode = mode;
                enterprise.IamSwitchedAt = now;
                enterprise.UpdatedAt = now;
                _audit.Add(Audit(caller, enterpriseId, enterpriseId,
                    new Dictionary<string, AuditChange> { ["IamMode"] = new(before.ToString(), mode.ToString(), null) }));
                await _db.SaveChangesAsync();
                await _db.User.Where(u => u.EnterpriseId == enterpriseId)
                    .ExecuteUpdateAsync(set => set.SetProperty(u => u.IamVersion, u => u.IamVersion + 1));

                await EnsureManagerRemainsAsync(enterpriseId, mode);
            });

            return mode;
        }

        public async Task<IReadOnlyList<HolderDTO>> HoldersAsync(Guid enterpriseId, string permission)
        {
            if (!IamCatalog.ByPermission.ContainsKey(permission))
                throw new IamException("not_grantable", $"A permissão {permission} não existe.", StatusCodes.Status404NotFound);

            var (users, grants, mode) = await StateAsync(enterpriseId);
            var byUser = grants.ToLookup(g => g.UserId);
            var bootstrap = mode == EnumIamMode.Grants && !await HasLiveIamManagerAsync(enterpriseId, Now);
            return users
                .Select(u => (User: u, Own: byUser[u.Id].FirstOrDefault(g => string.Equals(g.Permission, permission, StringComparison.OrdinalIgnoreCase)),
                    Held: PermissionEvaluator.Compose(mode, u.AccessLevel, byUser[u.Id].Select(g => (g.Permission, g.Effect))).Contains(permission)
                        || (bootstrap && u.AccessLevel == (short)EnumAccessLevel.Admin
                            && (permission == RbacPermissions.Iam.Read || permission == RbacPermissions.Iam.Manage))))
                .Where(x => x.Held)
                .Select(x => new HolderDTO(x.User.Id, $"{x.User.FirstName} {x.User.LastName}".Trim(),
                    HolderSource(x.User, x.Own, permission, bootstrap),
                    x.Own?.Effect == EnumGrantEffect.Allow ? x.Own.ExpiresAt : null))
                .OrderBy(h => h.Name, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Active users, their live (unexpired) grants of every effect, and the mode.</summary>
        private async Task<(List<User> Users, List<UserPermissionGrant> Grants, EnumIamMode Mode)> StateAsync(Guid enterpriseId)
        {
            var now = Now;
            var users = await _db.User.AsNoTracking().Where(u => u.EnterpriseId == enterpriseId && u.IsActive == true).ToListAsync();
            var ids = users.Select(u => u.Id).ToHashSet();
            var grants = (await _db.UserPermissionGrant.AsNoTracking()
                    .Where(g => g.EnterpriseId == enterpriseId && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now))
                    .ToListAsync())
                .Where(g => ids.Contains(g.UserId))
                .ToList();
            return (users, grants, await ModeAsync(enterpriseId));
        }

        public async Task<AccessReviewDTO> ReviewAsync(Guid enterpriseId)
        {
            var now = Now;
            var (users, grants, mode) = await StateAsync(enterpriseId);
            var byId = users.ToDictionary(u => u.Id);
            string NameOf(Guid id) => byId.TryGetValue(id, out var u) ? $"{u.FirstName} {u.LastName}".Trim() : string.Empty;

            var unused = grants
                .Where(g => g.Effect == EnumGrantEffect.Allow && IamCatalog.ByPermission.ContainsKey(g.Permission))
                .Select(g => (Grant: g, Idle: (int)(now - (g.LastUsedAt ?? g.CreatedAt)).TotalDays))
                .Where(x => x.Idle >= 30)
                .Select(x => new UnusedGrantDTO(x.Grant.UserId, NameOf(x.Grant.UserId), x.Grant.Permission, x.Grant.LastUsedAt, x.Grant.CreatedAt,
                    x.Idle, x.Idle >= 90 ? 90 : x.Idle >= 60 ? 60 : 30))
                .OrderByDescending(x => x.IdleDays).ThenBy(x => x.Name, StringComparer.Ordinal)
                .ToList();

            var unknown = grants
                .Where(g => !IamCatalog.ByPermission.ContainsKey(g.Permission))
                .Select(g => new UnknownGrantDTO(g.UserId, NameOf(g.UserId), g.Permission))
                .ToList();

            bool Sensitive(string p) => IamCatalog.TierOf(p) != PermissionTier.Standard && !p.StartsWith(PdvTokenScope.PermissionPrefix, StringComparison.Ordinal);
            var byUser = grants.ToLookup(g => g.UserId);
            var bootstrap = mode == EnumIamMode.Grants && !await HasLiveIamManagerAsync(enterpriseId, now);
            var held = users.ToDictionary(u => u.Id, u =>
            {
                var permissions = PermissionEvaluator.Compose(mode, u.AccessLevel, byUser[u.Id].Select(g => (g.Permission, g.Effect)));
                if (bootstrap && u.AccessLevel == (short)EnumAccessLevel.Admin)
                    permissions.UnionWith([RbacPermissions.Iam.Read, RbacPermissions.Iam.Manage]);
                return permissions.Where(Sensitive).ToList();
            });
            var withoutMfa = held
                .Where(kv => kv.Value.Count > 0 && byId.TryGetValue(kv.Key, out var u) && !u.MfaEnabled)
                .Select(kv => new MfaGapDTO(kv.Key, NameOf(kv.Key), [.. kv.Value.OrderBy(p => p, StringComparer.Ordinal)]))
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .ToList();

            return new AccessReviewDTO(unused, unknown, withoutMfa);
        }

        public async Task<EnumIamMode> ModeAsync(Guid enterpriseId) =>
            await _db.Enterprise.AsNoTracking().Where(e => e.Id == enterpriseId).Select(e => e.IamMode).FirstOrDefaultAsync();

        private async Task<bool> IsBootstrapAdminAsync(Guid enterpriseId, EnumIamMode mode, short accessLevel, DateTime now) =>
            mode == EnumIamMode.Grants && accessLevel == (short)EnumAccessLevel.Admin
            && !await HasLiveIamManagerAsync(enterpriseId, now);

        private Task<bool> HasLiveIamManagerAsync(Guid enterpriseId, DateTime now) =>
            _db.UserPermissionGrant.IgnoreQueryFilters().AsNoTracking().AnyAsync(g => g.EnterpriseId == enterpriseId
                && g.Permission == RbacPermissions.Iam.Manage && g.Effect == EnumGrantEffect.Allow
                && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now));

        private static string HolderSource(User user, UserPermissionGrant? grant, string permission, bool bootstrap)
        {
            if (grant?.Effect == EnumGrantEffect.Allow)
                return "grant";
            if (bootstrap && user.AccessLevel == (short)EnumAccessLevel.Admin
                && (permission == RbacPermissions.Iam.Read || permission == RbacPermissions.Iam.Manage))
                return "bootstrap";
            return IamCatalog.Baseline.Contains(permission) ? "baseline" : "role";
        }

        private sealed record Wanted(string Permission, GrantState State, DateTime? ExpiresAt, string? Reason);

        /// <summary>
        /// Holding <c>iam.manage</c> is the authority to grant: a privileged permission, MFA-gated and
        /// audited. A "grant only what you hold" ceiling would leave a tenant that started from none with
        /// nobody able to grant anything.
        /// </summary>
        private IReadOnlyList<Wanted> Validate(IReadOnlyList<GrantChange> changes)
        {
            var now = Now;
            var wanted = new Dictionary<string, Wanted>(StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes)
            {
                IReadOnlyList<string> targets;
                var hasModule = !string.IsNullOrWhiteSpace(change.Module);
                var hasPermission = !string.IsNullOrWhiteSpace(change.Permission);
                if (hasModule == hasPermission)
                    throw new IamException("invalid_grant_target", "Informe exatamente uma permissão ou um módulo por alteração.");

                if (hasModule)
                {
                    targets = IamCatalog.ExpandModule(change.Module!);
                    if (targets.Count == 0)
                        throw new IamException("not_grantable", $"O módulo {change.Module} não tem permissões concedíveis.");
                }
                else if (!string.IsNullOrWhiteSpace(change.Permission) && IamCatalog.IsGrantable(change.Permission))
                {
                    targets = [IamCatalog.ByPermission[change.Permission].Permission];
                }
                else
                {
                    throw new IamException("not_grantable", $"A permissão {change.Permission} não pode ser concedida.");
                }

                var reason = string.IsNullOrWhiteSpace(change.Reason) ? null : change.Reason.Trim();
                if (reason?.Length > MaxReasonLength)
                    throw new IamException("reason_too_long", $"O motivo pode ter até {MaxReasonLength} caracteres.");

                var setsSomething = change.State != GrantState.Inherit;
                if (setsSomething && change.ExpiresAt is { } expires && (expires <= now || expires > now + MaxGrantLifetime))
                    throw new IamException("invalid_expiry", "A validade deve estar no futuro e em até um ano.");

                foreach (var permission in targets)
                    wanted[permission] = new Wanted(permission, change.State, setsSomething ? change.ExpiresAt : null, reason);
            }

            foreach (var item in wanted.Values.Where(w => w.State == GrantState.Allow))
            {
                var entry = IamCatalog.ByPermission[item.Permission];
                if (entry.Tier != PermissionTier.Standard && item.Reason == null)
                    throw new IamException("reason_required", $"Informe o motivo para conceder \"{entry.Label}\".");
            }

            return [.. wanted.Values];
        }

        /// <summary>Applies the changes to the target's live grants; true when anything changed. Caller holds the lock.</summary>
        private async Task<bool> WriteAsync(User caller, User target, IReadOnlyList<Wanted> wanted)
        {
            var now = Now;
            var live = (await _db.UserPermissionGrant
                    .Where(g => g.UserId == target.Id && g.RevokedAt == null)
                    .ToListAsync())
                .ToDictionary(g => g.Permission, StringComparer.OrdinalIgnoreCase);
            var changes = new Dictionary<string, AuditChange>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in wanted)
            {
                live.TryGetValue(item.Permission, out var grant);
                var expired = grant?.ExpiresAt is { } until && until <= now;
                if (item.State != GrantState.Inherit)
                {
                    var effect = item.State == GrantState.Deny ? EnumGrantEffect.Deny : EnumGrantEffect.Allow;
                    if (grant != null && !expired && grant.Effect == effect && grant.ExpiresAt == item.ExpiresAt && grant.Reason == item.Reason)
                        continue;

                    var before = grant == null || expired ? null : Describe(grant.Effect, grant.ExpiresAt);
                    if (grant != null)
                    {
                        grant.RevokedAt = now;
                        grant.RevokedByUserId = caller.Id;
                        grant.UpdatedAt = now;
                        grant = null;
                    }

                    _db.UserPermissionGrant.Add(new UserPermissionGrant
                    {
                        Id = Guid.NewGuid(),
                        EnterpriseId = target.EnterpriseId!.Value,
                        UserId = target.Id,
                        Permission = item.Permission,
                        Effect = effect,
                        ExpiresAt = item.ExpiresAt,
                        Reason = item.Reason,
                        GrantedByUserId = caller.Id,
                        CreatedAt = now,
                        IsActive = true
                    });

                    changes[item.Permission] = new AuditChange(before, Describe(effect, item.ExpiresAt), item.Reason);
                }
                else if (grant != null)
                {
                    grant.RevokedAt = now;
                    grant.RevokedByUserId = caller.Id;
                    grant.UpdatedAt = now;
                    if (!expired)
                        changes[item.Permission] = new AuditChange(Describe(grant.Effect, grant.ExpiresAt), null, item.Reason);
                }
            }

            if (changes.Count == 0 && !_db.ChangeTracker.HasChanges())
                return false;

            target.IamVersion++;
            if (changes.Count > 0)
                _audit.Add(Audit(caller, target.EnterpriseId!.Value, target.Id, changes));
            await _db.SaveChangesAsync();
            return changes.Count > 0;
        }

        private async Task EnsureIamGrantsAsync(Guid enterpriseId, Guid userId, Guid grantedBy, DateTime now)
        {
            var held = (await _db.UserPermissionGrant
                    .Where(g => g.UserId == userId && g.RevokedAt == null && g.Effect == EnumGrantEffect.Allow && g.ExpiresAt == null)
                    .Select(g => g.Permission)
                    .ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var permission in new[] { RbacPermissions.Iam.Read, RbacPermissions.Iam.Manage }.Where(p => !held.Contains(p)))
            {
                var stale = await _db.UserPermissionGrant.FirstOrDefaultAsync(g => g.UserId == userId && g.Permission == permission && g.RevokedAt == null);
                if (stale != null)
                {
                    stale.RevokedAt = now;
                    stale.RevokedByUserId = grantedBy;
                    stale.UpdatedAt = now;
                }

                _db.UserPermissionGrant.Add(new UserPermissionGrant
                {
                    Id = Guid.NewGuid(),
                    EnterpriseId = enterpriseId,
                    UserId = userId,
                    Permission = permission,
                    Reason = SwitchReason,
                    GrantedByUserId = grantedBy,
                    CreatedAt = now,
                    IsActive = true
                });
            }
        }

        /// <summary>
        /// Refuses a state with nobody who can still manage access. Both modes require an active manager
        /// with MFA; grants mode additionally requires an open-ended iam.manage allow.
        /// </summary>
        private async Task EnsureManagerRemainsAsync(Guid enterpriseId, EnumIamMode mode, Guid? excludedUserId = null)
        {
            var now = Now;
            var manage = _db.UserPermissionGrant.Where(g =>
                g.EnterpriseId == enterpriseId && g.Permission == RbacPermissions.Iam.Manage && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now));
            var openAllow = manage.Where(g => g.Effect == EnumGrantEffect.Allow && g.ExpiresAt == null);

            var any = mode == EnumIamMode.Grants
                ? await openAllow.AnyAsync(g => g.UserId != excludedUserId && _db.User.Any(u => u.Id == g.UserId && u.IsActive == true && u.MfaEnabled))
                : await openAllow.AnyAsync(g => g.UserId != excludedUserId && _db.User.Any(u => u.Id == g.UserId && u.IsActive == true && u.MfaEnabled))
                  || await _db.User.AnyAsync(u => u.Id != excludedUserId && u.EnterpriseId == enterpriseId && u.IsActive == true && u.AccessLevel == (short)EnumAccessLevel.Admin
                                                  && u.MfaEnabled
                                                  && !manage.Any(g => g.UserId == u.Id && g.Effect == EnumGrantEffect.Deny));
            if (!any)
                throw new IamException("last_iam_manager", "A empresa ficaria sem ninguém com verificação em duas etapas para gerenciar acessos.", StatusCodes.Status409Conflict);
        }

        private sealed record AuditChange(string? OldValue, string? NewValue, string? Reason);

        private AuditTrail Audit<T>(User caller, Guid enterpriseId, Guid entityId, Dictionary<string, T> changes) => new()
        {
            UserId = caller.Id,
            EnterpriseId = enterpriseId,
            ChangedAt = Now,
            Action = "EDIT",
            EntityName = AuditedEntities.Iam,
            EntityId = entityId,
            ChangesJson = JsonSerializer.Serialize(changes)
        };

        private static string Describe(EnumGrantEffect effect, DateTime? expiresAt)
        {
            var what = effect == EnumGrantEffect.Deny ? "negado" : "concedido";
            return expiresAt is { } until ? $"{what} até {until:O}" : what;
        }

        public static string TierName(PermissionTier tier) => tier switch
        {
            PermissionTier.Privileged => "privileged",
            PermissionTier.Sensitive => "sensitive",
            _ => "standard"
        };

        private static IamException NotFound() => new("not_found", "Usuário não encontrado.", StatusCodes.Status404NotFound);
    }
}
