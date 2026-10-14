using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Caching.Distributed;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serilog;

namespace EvangelionERPV2.UserModule.Application.Services
{
    public sealed record MfaSetup(string Secret, string OtpauthUri);

    public sealed record MfaChallengeResult(User User, int? RecoveryCodesRemaining);

    /// <summary>
    /// Opt-in TOTP second factor: setup, the login challenge that stands between a proven password
    /// and issued tokens, recovery codes, and turning it off.
    /// </summary>
    public sealed class MfaService
    {
        private const string Issuer = "Evangelion ERP";
        private const int RecoveryCodeCount = 10;
        private const int MaxChallengeFailures = 5;
        private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
        private const int MaxAccountFailures = 10;
        private static readonly TimeSpan AccountFailureWindow = TimeSpan.FromMinutes(15);

        private readonly IRepository<User> _users;
        private readonly IRepository<MfaRecoveryCode> _recoveryCodes;
        private readonly IRepository<RefreshToken> _refreshTokens;
        private readonly IAuditTrailWriter _audit;
        private readonly IDistributedCache _cache;
        private readonly TimeProvider _time;

        public MfaService(
            IRepository<User> users,
            IRepository<MfaRecoveryCode> recoveryCodes,
            IRepository<RefreshToken> refreshTokens,
            IAuditTrailWriter audit,
            IDistributedCache cache,
            TimeProvider? time = null)
        {
            _users = users;
            _recoveryCodes = recoveryCodes;
            _refreshTokens = refreshTokens;
            _audit = audit;
            _cache = cache;
            _time = time ?? TimeProvider.System;
        }

        public static string FailuresKey(Guid userId) => "mfa:failures:" + userId.ToString("N");

        public static string ChallengeKey(string mfaToken) =>
            "mfa:challenge:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mfaToken)));

        public async Task<MfaSetup> BeginSetupAsync(User user)
        {
            if (user.MfaEnabled)
                throw new InvalidOperationException("Two-factor authentication is already enabled.");

            var secret = Totp.ToBase32(Totp.NewSecret()).TrimEnd('=');
            user.MfaPendingSecret = EncryptOrThrow(secret);
            _users.Update(user);
            await _users.CommitAsync();

            var issuer = Uri.EscapeDataString(Issuer);
            var account = Uri.EscapeDataString(user.Email);
            return new MfaSetup(secret, $"otpauth://totp/{issuer}:{account}?secret={secret}&issuer={issuer}");
        }

        public Task<IReadOnlyList<string>?> ConfirmSetupAsync(Guid userId, string? code) =>
            _users.ExecuteInTransactionAsync<IReadOnlyList<string>?>(async () =>
            {
                var user = await LoadLockedAsync(userId);
                if (user == null || user.MfaEnabled || string.IsNullOrEmpty(user.MfaPendingSecret))
                    return null;

                var step = Totp.MatchStep(DecryptSecret(user.MfaPendingSecret), code, CurrentStep, lastUsedStep: null);
                if (step == null)
                    return null;

                user.MfaSecret = user.MfaPendingSecret;
                user.MfaPendingSecret = null;
                user.MfaEnabled = true;
                user.MfaLastUsedStep = step;
                _users.Update(user);
                var codes = await ReplaceRecoveryCodesAsync(user.Id);
                Audit(user, user.Id, ("MfaEnabled", false, true));
                await _users.CommitAsync();
                return codes;
            });

        public Task<bool> DisableAsync(Guid userId, string? code) =>
            _users.ExecuteInTransactionAsync(async () =>
            {
                var user = await LoadLockedAsync(userId);
                if (user == null || !user.MfaEnabled || await ProveAsync(user, code) == null)
                    return false;

                await TurnOffAsync(user, user.Id, "Turned off by the user");
                return true;
            });

        public Task<IReadOnlyList<string>?> RegenerateRecoveryCodesAsync(Guid userId, string? code) =>
            _users.ExecuteInTransactionAsync<IReadOnlyList<string>?>(async () =>
            {
                var user = await LoadLockedAsync(userId);
                if (user == null || !user.MfaEnabled || await ProveAsync(user, code) == null)
                    return null;

                var codes = await ReplaceRecoveryCodesAsync(user.Id);
                Audit(user, user.Id, ("RecoveryCodes", null, "Regenerated"));
                await _users.CommitAsync();
                return codes;
            });

        /// <summary>Turns MFA off for a locked-out user; <paramref name="actorId"/> is the administrator.</summary>
        public Task ResetAsync(Guid userId, Guid? actorId) =>
            _users.ExecuteInTransactionAsync(async () =>
            {
                var user = await LoadLockedAsync(userId);
                if (user == null)
                    return false;

                await TurnOffAsync(user, actorId, "Reset by an administrator");
                return true;
            });

        public async Task<string> CreateChallengeAsync(Guid userId)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
            await SaveChallengeAsync(token, new Challenge(userId, 0, _time.GetUtcNow() + ChallengeLifetime));
            return token;
        }

        /// <summary>
        /// The challenged user when <paramref name="code"/> is a valid TOTP or unused recovery code;
        /// null otherwise. The challenge is consumed on success and on the fifth failure, and every code is
        /// refused for the rest of a 15-minute window once the account has failed ten times in it, so fresh
        /// challenges from new logins cannot keep the guessing going.
        /// </summary>
        public async Task<MfaChallengeResult?> VerifyChallengeAsync(string? mfaToken, string? code)
        {
            if (string.IsNullOrWhiteSpace(mfaToken))
                return null;

            var key = ChallengeKey(mfaToken);
            var challenge = await ReadChallengeAsync(key);
            if (challenge == null)
                return null;

            return await _users.ExecuteInTransactionAsync<MfaChallengeResult?>(async () =>
            {
                var user = await LoadLockedAsync(challenge.UserId);

                // Re-read under the user's row lock: a parallel request that already spent this
                // challenge removed it before releasing the lock, so it is single-use.
                var current = await ReadChallengeAsync(key);
                if (current == null)
                    return null;

                if (user == null || !user.MfaEnabled)
                {
                    await _cache.RemoveAsync(key);
                    return null;
                }

                var failures = await ReadFailuresAsync(user.Id);
                if (failures?.Count >= MaxAccountFailures)
                {
                    Log.Logger.Warning("MFA code refused: account failure limit reached. UserId={UserId}", user.Id);
                    await _cache.RemoveAsync(key);
                    return null;
                }

                var proof = await ProveAsync(user, code);
                if (proof == null)
                {
                    var now = _time.GetUtcNow();
                    var updatedFailures = failures is null
                        ? new Failures(1, now + AccountFailureWindow)
                        : failures with { Count = failures.Count + 1 };
                    if (updatedFailures.Count >= MaxAccountFailures)
                        updatedFailures = updatedFailures with { WindowEndsAt = now + AccountFailureWindow };

                    await SaveFailuresAsync(user.Id, updatedFailures);
                    Log.Logger.Warning(
                        "MFA code rejected. UserId={UserId}, Failures={Failures}, ChallengeSpent={Spent}",
                        user.Id, current.Failures + 1, current.Failures + 1 >= MaxChallengeFailures);
                    if (current.Failures + 1 >= MaxChallengeFailures)
                        await _cache.RemoveAsync(key);
                    else
                        await SaveChallengeAsync(mfaToken, current with { Failures = current.Failures + 1 });
                    return null;
                }

                await _cache.RemoveAsync(key);
                await _users.CommitAsync();
                return new MfaChallengeResult(user, proof.RecoveryCodesRemaining);
            });
        }

        /// <summary>
        /// True when <paramref name="code"/> has the shape of a TOTP or a recovery code. Lets a caller
        /// refuse a typo without spending one of the challenge's attempts.
        /// </summary>
        public static bool IsWellFormedCode(string? code)
        {
            var digits = (code ?? string.Empty).Replace(" ", string.Empty);
            return (digits.Length == 6 && digits.All(char.IsAsciiDigit)) || Totp.NormalizeRecoveryCode(code) != null;
        }

        private async Task<Challenge?> ReadChallengeAsync(string key)
        {
            var raw = await _cache.GetStringAsync(key);
            var challenge = raw == null ? null : JsonSerializer.Deserialize<Challenge>(raw);
            if (challenge != null && challenge.ExpiresAt > _time.GetUtcNow())
                return challenge;

            if (raw != null)
                await _cache.RemoveAsync(key);
            return null;
        }

        private async Task<Failures?> ReadFailuresAsync(Guid userId)
        {
            var raw = await _cache.GetStringAsync(FailuresKey(userId));
            var failures = raw == null ? null : JsonSerializer.Deserialize<Failures>(raw);
            return failures != null && failures.WindowEndsAt > _time.GetUtcNow() ? failures : null;
        }

        private Task SaveFailuresAsync(Guid userId, Failures failures) =>
            _cache.SetStringAsync(FailuresKey(userId), JsonSerializer.Serialize(failures),
                new DistributedCacheEntryOptions { AbsoluteExpiration = failures.WindowEndsAt });

        private sealed record Failures(int Count, DateTimeOffset WindowEndsAt);

        private long CurrentStep => Totp.CurrentStep(_time.GetUtcNow());

        private async Task<User?> LoadLockedAsync(Guid userId)
        {
            await _users.LockForUpdateAsync(userId);
            try
            {
                return await _users.GetByIdAsync(userId);
            }
            catch (NotFoundDatabaseException)
            {
                return null;
            }
        }

        private sealed record Proof(int? RecoveryCodesRemaining);

        /// <summary>Accepts a TOTP or a recovery code, recording its use; caller commits.</summary>
        private async Task<Proof?> ProveAsync(User user, string? code)
        {
            if (string.IsNullOrEmpty(user.MfaSecret))
                return null;

            var step = Totp.MatchStep(DecryptSecret(user.MfaSecret), code, CurrentStep, user.MfaLastUsedStep);
            if (step != null)
            {
                user.MfaLastUsedStep = step;
                _users.Update(user);
                return new Proof(null);
            }

            var recovery = Totp.NormalizeRecoveryCode(code);
            if (recovery == null)
                return null;

            var unused = (await _recoveryCodes.GetAllAsync(c => c.UserId == user.Id && c.UsedAt == null && c.IsActive == true)).ToList();
            var hash = HashRecoveryCode(recovery);
            var match = unused.FirstOrDefault(c => CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(c.CodeHash), Encoding.ASCII.GetBytes(hash)));
            if (match == null)
                return null;

            match.UsedAt = _time.GetUtcNow().UtcDateTime;
            _recoveryCodes.Update(match);
            Audit(user, user.Id, ("RecoveryCodesRemaining", unused.Count, unused.Count - 1));
            return new Proof(unused.Count - 1);
        }

        private async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(Guid userId)
        {
            _recoveryCodes.DeleteRange(await _recoveryCodes.GetAllAsync(c => c.UserId == userId));

            var codes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => Totp.NewRecoveryCode()).ToList();
            await _recoveryCodes.CreateRangeAsync(codes.Select(code => new MfaRecoveryCode
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CodeHash = HashRecoveryCode(Totp.NormalizeRecoveryCode(code)!)
            }).ToList());
            return codes;
        }

        private async Task TurnOffAsync(User user, Guid? actorId, string reason)
        {
            Audit(user, actorId, ("MfaEnabled", user.MfaEnabled, false), ("Reason", null, reason));
            user.MfaEnabled = false;
            user.MfaSecret = null;
            user.MfaPendingSecret = null;
            user.MfaLastUsedStep = null;
            _users.Update(user);
            _recoveryCodes.DeleteRange(await _recoveryCodes.GetAllAsync(c => c.UserId == user.Id));

            var now = _time.GetUtcNow().UtcDateTime;
            foreach (var token in await _refreshTokens.GetAllAsync(t => t.UserId == user.Id && t.IsActive == true && t.RevokedAt == null))
            {
                token.RevokedAt = now;
                token.IsActive = false;
                token.UpdatedAt = now;
                _refreshTokens.Update(token);
            }

            await _users.CommitAsync();
        }

        private void Audit(User user, Guid? actorId, params (string Field, object? Old, object? New)[] changes) =>
            _audit.Add(new AuditTrail
            {
                UserId = actorId,
                EnterpriseId = user.EnterpriseId,
                ChangedAt = _time.GetUtcNow().UtcDateTime,
                Action = "EDIT",
                EntityName = AuditedEntities.Mfa,
                EntityId = user.Id,
                ChangesJson = JsonSerializer.Serialize(changes.ToDictionary(c => c.Field, c => new { OldValue = c.Old, NewValue = c.New }))
            });

        private Task SaveChallengeAsync(string mfaToken, Challenge challenge) =>
            _cache.SetStringAsync(ChallengeKey(mfaToken), JsonSerializer.Serialize(challenge),
                new DistributedCacheEntryOptions { AbsoluteExpiration = challenge.ExpiresAt });

        private sealed record Challenge(Guid UserId, int Failures, DateTimeOffset ExpiresAt);

        /// <summary>Recovery codes carry 50 random bits, so a fast unsalted hash is enough.</summary>
        private static string HashRecoveryCode(string normalized) =>
            Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(normalized)));

        private static byte[] DecryptSecret(string encrypted)
        {
            var base32 = SharedFunctions.Decrypt(encrypted);
            if (string.IsNullOrEmpty(base32))
                throw new InvalidOperationException("Two-factor secret could not be decrypted.");

            return Totp.FromBase32(base32);
        }

        private static string EncryptOrThrow(string value)
        {
            var encrypted = SharedFunctions.Encrypt(value);
            if (string.IsNullOrEmpty(encrypted))
                throw new InvalidOperationException("Two-factor secret could not be encrypted.");

            return encrypted;
        }
    }
}
