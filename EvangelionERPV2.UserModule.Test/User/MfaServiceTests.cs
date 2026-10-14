using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using System.Linq.Expressions;
using System.Reflection;
using UserEntity = EvangelionERPV2.Shared.Entities.User;

namespace EvangelionERPV2.UserModule.Test.User
{
    [Collection(SharedFunctionsKeyCollection.Name)]
    public class MfaServiceTests
    {
        private readonly List<UserEntity> _users = new();
        private readonly List<MfaRecoveryCode> _codes = new();
        private readonly List<RefreshToken> _refreshTokens = new();
        private readonly MemoryDistributedCache _cache = new(Options.Create(new MemoryDistributedCacheOptions()));
        private readonly FixedTime _time = new(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        private readonly MfaService _service;
        private readonly Mock<IRepository<UserEntity>> _userRepo = new();
        private readonly Mock<IAuditTrailWriter> _audit = new();
        private readonly List<AuditTrail> _auditRows = new();
        private readonly UserEntity _user;

        public MfaServiceTests()
        {
            EnsureEncryptionKeyInitialized();

            var users = _userRepo;
            users.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id) => _users.FirstOrDefault(u => u.Id == id) ?? throw new NotFoundDatabaseException());
            users.Setup(r => r.Update(It.IsAny<UserEntity>())).Returns((UserEntity u) => u);
            users.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task<bool>> op, CancellationToken _) => op());
            users.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task<IReadOnlyList<string>?>>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task<IReadOnlyList<string>?>> op, CancellationToken _) => op());
            users.Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task<MfaChallengeResult?>>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task<MfaChallengeResult?>> op, CancellationToken _) => op());

            var codes = new Mock<IRepository<MfaRecoveryCode>>();
            codes.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<MfaRecoveryCode, bool>>>()))
                .ReturnsAsync((Expression<Func<MfaRecoveryCode, bool>> p) => _codes.Where(p.Compile()).ToList());
            codes.Setup(r => r.CreateRangeAsync(It.IsAny<IEnumerable<MfaRecoveryCode>>()))
                .ReturnsAsync((IEnumerable<MfaRecoveryCode> e) => { var list = e.ToList(); _codes.AddRange(list); return list; });
            codes.Setup(r => r.DeleteRange(It.IsAny<IEnumerable<MfaRecoveryCode>>()))
                .Returns((IEnumerable<MfaRecoveryCode> e) => { var list = e.ToList(); _codes.RemoveAll(list.Contains); return list; });
            codes.Setup(r => r.Update(It.IsAny<MfaRecoveryCode>())).Returns((MfaRecoveryCode c) => c);

            var refreshTokens = new Mock<IRepository<RefreshToken>>();
            refreshTokens.Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<RefreshToken, bool>>>()))
                .ReturnsAsync((Expression<Func<RefreshToken, bool>> p) => _refreshTokens.Where(p.Compile()).ToList());
            refreshTokens.Setup(r => r.Update(It.IsAny<RefreshToken>())).Returns((RefreshToken t) => t);

            _audit.Setup(a => a.Add(It.IsAny<AuditTrail>())).Callback((AuditTrail row) => _auditRows.Add(row));
            _user = new UserEntity { Id = Guid.NewGuid(), Email = "ana@example.com", IsActive = true, EnterpriseId = Guid.NewGuid() };
            _users.Add(_user);
            _service = new MfaService(users.Object, codes.Object, refreshTokens.Object, _audit.Object, _cache, _time);
        }

        private static readonly Guid AdminId = Guid.NewGuid();

        private long Step => Totp.CurrentStep(_time.GetUtcNow());

        private AuditTrail LastAudit()
        {
            var row = _auditRows.Last();
            Assert.Equal(AuditedEntities.Mfa, row.EntityName);
            Assert.Equal("EDIT", row.Action);
            Assert.Equal(_user.Id, row.EntityId);
            Assert.Equal(_user.EnterpriseId, row.EnterpriseId);
            return row;
        }

        private static System.Text.Json.JsonElement Change(AuditTrail row, string field) =>
            System.Text.Json.JsonDocument.Parse(row.ChangesJson).RootElement.GetProperty(field);

        private static byte[] SecretOf(MfaSetup setup) => Totp.FromBase32(setup.Secret);

        private async Task<(byte[] Secret, IReadOnlyList<string> RecoveryCodes)> EnableAsync()
        {
            var setup = await _service.BeginSetupAsync(_user);
            var codes = await _service.ConfirmSetupAsync(_user.Id, Totp.Code(SecretOf(setup), Step));
            _time.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));
            return (SecretOf(setup), codes!);
        }

        [Fact]
        public async Task BeginSetup_StoresEncryptedPendingSecretAndLeavesMfaOff()
        {
            var setup = await _service.BeginSetupAsync(_user);

            Assert.Equal(20, SecretOf(setup).Length);
            Assert.False(_user.MfaEnabled);
            Assert.Null(_user.MfaSecret);
            Assert.NotNull(_user.MfaPendingSecret);
            Assert.DoesNotContain(setup.Secret, _user.MfaPendingSecret);
            Assert.Equal(setup.Secret, SharedFunctions.Decrypt(_user.MfaPendingSecret!));
            Assert.Equal($"otpauth://totp/Evangelion%20ERP:ana%40example.com?secret={setup.Secret}&issuer=Evangelion%20ERP", setup.OtpauthUri);
        }

        [Fact]
        public async Task BeginSetup_AgainReplacesThePendingSecret()
        {
            var first = await _service.BeginSetupAsync(_user);
            await _service.BeginSetupAsync(_user);

            Assert.Null(await _service.ConfirmSetupAsync(_user.Id, Totp.Code(SecretOf(first), Step)));
            Assert.False(_user.MfaEnabled);
        }

        [Fact]
        public async Task ConfirmSetup_WithValidCode_EnablesAndReturnsTenRecoveryCodesOnce()
        {
            var setup = await _service.BeginSetupAsync(_user);

            var codes = await _service.ConfirmSetupAsync(_user.Id, Totp.Code(SecretOf(setup), Step));

            Assert.NotNull(codes);
            Assert.Equal(10, codes!.Count);
            Assert.Equal(10, codes.Distinct().Count());
            Assert.True(_user.MfaEnabled);
            Assert.Null(_user.MfaPendingSecret);
            Assert.Equal(setup.Secret, SharedFunctions.Decrypt(_user.MfaSecret!));
            Assert.Equal(10, _codes.Count(c => c.UserId == _user.Id));
            Assert.All(_codes, c => Assert.DoesNotContain(c.CodeHash, codes.Select(Totp.NormalizeRecoveryCode)));
        }

        [Fact]
        public async Task ConfirmSetup_WithWrongCode_ChangesNothing()
        {
            var setup = await _service.BeginSetupAsync(_user);
            var pending = _user.MfaPendingSecret;
            var wrong = Totp.Code(SecretOf(setup), Step + 5);

            Assert.Null(await _service.ConfirmSetupAsync(_user.Id, wrong));
            Assert.False(_user.MfaEnabled);
            Assert.Equal(pending, _user.MfaPendingSecret);
            Assert.Empty(_codes);
        }

        [Fact]
        public async Task BeginSetup_WhenAlreadyEnabled_Throws()
        {
            await EnableAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.BeginSetupAsync(_user));
        }

        [Fact]
        public async Task Challenge_WithValidCode_ReturnsUserAndIsSingleUse()
        {
            var (secret, _) = await EnableAsync();
            var token = await _service.CreateChallengeAsync(_user.Id);

            var result = await _service.VerifyChallengeAsync(token, Totp.Code(secret, Step));

            Assert.Same(_user, result!.User);
            Assert.Null(result.RecoveryCodesRemaining);
            Assert.Equal(Step, _user.MfaLastUsedStep);
            _time.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));
            Assert.Null(await _service.VerifyChallengeAsync(token, Totp.Code(secret, Step)));
        }

        [Fact]
        public async Task Challenge_RejectsACodeFromAStepAlreadyUsed()
        {
            var (secret, _) = await EnableAsync();
            var code = Totp.Code(secret, Step);
            Assert.NotNull(await _service.VerifyChallengeAsync(await _service.CreateChallengeAsync(_user.Id), code));

            Assert.Null(await _service.VerifyChallengeAsync(await _service.CreateChallengeAsync(_user.Id), code));
        }

        [Fact]
        public async Task Challenge_DiesOnTheFifthFailure()
        {
            var (secret, _) = await EnableAsync();
            var token = await _service.CreateChallengeAsync(_user.Id);
            var wrong = Totp.Code(secret, Step + 5);

            for (var i = 0; i < 4; i++)
                Assert.Null(await _service.VerifyChallengeAsync(token, wrong));
            Assert.NotNull(await _cache.GetAsync(MfaService.ChallengeKey(token)));

            Assert.Null(await _service.VerifyChallengeAsync(token, wrong));
            Assert.Null(await _cache.GetAsync(MfaService.ChallengeKey(token)));
            Assert.Null(await _service.VerifyChallengeAsync(token, Totp.Code(secret, Step)));
        }

        [Fact]
        public async Task Challenge_AfterTenFailuresAcrossChallenges_RefusesEvenTheRightCodeUntilTheWindowEnds()
        {
            var (secret, _) = await EnableAsync();
            var wrong = Totp.Code(secret, Step + 5);
            for (var login = 0; login < 2; login++)
            {
                var token = await _service.CreateChallengeAsync(_user.Id);
                for (var i = 0; i < 5; i++)
                    Assert.Null(await _service.VerifyChallengeAsync(token, wrong));
            }

            Assert.Null(await _service.VerifyChallengeAsync(await _service.CreateChallengeAsync(_user.Id), Totp.Code(secret, Step)));

            _time.Advance(TimeSpan.FromMinutes(15));
            Assert.NotNull(await _service.VerifyChallengeAsync(await _service.CreateChallengeAsync(_user.Id), Totp.Code(secret, Step)));
        }

        [Fact]
        public async Task Challenge_NineFailuresStillLetTheRightCodeIn()
        {
            var (secret, _) = await EnableAsync();
            var wrong = Totp.Code(secret, Step + 5);
            var first = await _service.CreateChallengeAsync(_user.Id);
            for (var i = 0; i < 5; i++)
                Assert.Null(await _service.VerifyChallengeAsync(first, wrong));
            var second = await _service.CreateChallengeAsync(_user.Id);
            for (var i = 0; i < 4; i++)
                Assert.Null(await _service.VerifyChallengeAsync(second, wrong));

            Assert.NotNull(await _service.VerifyChallengeAsync(second, Totp.Code(secret, Step)));
        }

        [Fact]
        public async Task Challenge_ExpiresAfterFiveMinutes()
        {
            var (secret, _) = await EnableAsync();
            var token = await _service.CreateChallengeAsync(_user.Id);

            _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

            Assert.Null(await _service.VerifyChallengeAsync(token, Totp.Code(secret, Step)));
        }

        [Fact]
        public async Task Challenge_UnknownTokenIsRejected()
        {
            var (secret, _) = await EnableAsync();

            Assert.Null(await _service.VerifyChallengeAsync("not-a-token", Totp.Code(secret, Step)));
            Assert.Null(await _service.VerifyChallengeAsync(null, Totp.Code(secret, Step)));
        }

        [Fact]
        public async Task Challenge_KeyDoesNotContainTheRawToken()
        {
            var token = await _service.CreateChallengeAsync(_user.Id);

            Assert.DoesNotContain(token, MfaService.ChallengeKey(token));
        }

        [Fact]
        public async Task Challenge_AcceptsARecoveryCodeOnceAndReportsWhatIsLeft()
        {
            var (_, recovery) = await EnableAsync();

            var result = await _service.VerifyChallengeAsync(await _service.CreateChallengeAsync(_user.Id), recovery[3].ToLowerInvariant());

            Assert.Same(_user, result!.User);
            Assert.Equal(9, result.RecoveryCodesRemaining);
            Assert.Null(await _service.VerifyChallengeAsync(await _service.CreateChallengeAsync(_user.Id), recovery[3]));
        }

        [Fact]
        public async Task Challenge_ForAUserWhoTurnedMfaOffIsRejected()
        {
            var (secret, _) = await EnableAsync();
            var token = await _service.CreateChallengeAsync(_user.Id);
            await _service.ResetAsync(_user.Id, AdminId);

            Assert.Null(await _service.VerifyChallengeAsync(token, Totp.Code(secret, Step)));
        }

        [Fact]
        public async Task Challenge_PersistsWhatTheProofChanged()
        {
            var (secret, _) = await EnableAsync();
            var token = await _service.CreateChallengeAsync(_user.Id);
            _userRepo.Invocations.Clear();

            Assert.NotNull(await _service.VerifyChallengeAsync(token, Totp.Code(secret, Step)));

            _userRepo.Verify(r => r.LockForUpdateAsync(_user.Id, It.IsAny<CancellationToken>()), Times.Once);
            _userRepo.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Challenge_SpentByOneFactorRejectsAnother()
        {
            var (secret, recovery) = await EnableAsync();
            var token = await _service.CreateChallengeAsync(_user.Id);

            Assert.NotNull(await _service.VerifyChallengeAsync(token, Totp.Code(secret, Step)));

            Assert.Null(await _service.VerifyChallengeAsync(token, recovery[0]));
            Assert.Equal(10, _codes.Count(c => c.UsedAt == null));
        }

        [Fact]
        public async Task Challenge_SpentWhileWaitingForTheLockIsRejected()
        {
            var (_, recovery) = await EnableAsync();
            var token = await _service.CreateChallengeAsync(_user.Id);
            _userRepo.Setup(r => r.LockForUpdateAsync(_user.Id, It.IsAny<CancellationToken>()))
                .Returns(() => _cache.RemoveAsync(MfaService.ChallengeKey(token)));

            Assert.Null(await _service.VerifyChallengeAsync(token, recovery[0]));
            Assert.All(_codes, c => Assert.Null(c.UsedAt));
        }

        [Theory]
        [InlineData("123456", true)]
        [InlineData("123 456", true)]
        [InlineData("abcde-fghij", true)]
        [InlineData("12345", false)]
        [InlineData("1234567", false)]
        [InlineData("12a456", false)]
        [InlineData("ABCDE-FGH1J", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsWellFormedCode_AcceptsOnlyTotpOrRecoveryShapes(string? code, bool expected)
        {
            Assert.Equal(expected, MfaService.IsWellFormedCode(code));
        }

        [Fact]
        public async Task Challenge_ForADeletedUserIsRejected()
        {
            var (secret, _) = await EnableAsync();
            var token = await _service.CreateChallengeAsync(_user.Id);
            _users.Clear();

            Assert.Null(await _service.VerifyChallengeAsync(token, Totp.Code(secret, Step)));
        }

        [Fact]
        public async Task Disable_WithValidCode_ClearsEverythingAndRevokesRefreshTokens()
        {
            var (secret, _) = await EnableAsync();
            var refresh = new RefreshToken { UserId = _user.Id, IsActive = true, ExpiresAt = DateTime.UtcNow.AddDays(1) };
            var otherUsers = new RefreshToken { UserId = Guid.NewGuid(), IsActive = true, ExpiresAt = DateTime.UtcNow.AddDays(1) };
            _refreshTokens.AddRange(new[] { refresh, otherUsers });

            Assert.True(await _service.DisableAsync(_user.Id, Totp.Code(secret, Step)));

            Assert.False(_user.MfaEnabled);
            Assert.Null(_user.MfaSecret);
            Assert.Null(_user.MfaPendingSecret);
            Assert.Null(_user.MfaLastUsedStep);
            Assert.Empty(_codes);
            Assert.NotNull(refresh.RevokedAt);
            Assert.False(refresh.IsActive);
            Assert.Null(otherUsers.RevokedAt);
        }

        [Fact]
        public async Task Disable_AcceptsARecoveryCode()
        {
            var (_, recovery) = await EnableAsync();

            Assert.True(await _service.DisableAsync(_user.Id, recovery[0]));
            Assert.False(_user.MfaEnabled);
        }

        [Fact]
        public async Task Disable_WithInvalidCode_ChangesNothing()
        {
            var (secret, _) = await EnableAsync();
            var stored = _user.MfaSecret;

            Assert.False(await _service.DisableAsync(_user.Id, Totp.Code(secret, Step + 5)));
            Assert.False(await _service.DisableAsync(_user.Id, "AAAAA-AAAAA"));
            Assert.True(_user.MfaEnabled);
            Assert.Equal(stored, _user.MfaSecret);
            Assert.Equal(10, _codes.Count);
        }

        [Fact]
        public async Task Regenerate_WithValidCode_ReplacesTheRecoveryCodes()
        {
            var (secret, oldCodes) = await EnableAsync();

            var fresh = await _service.RegenerateRecoveryCodesAsync(_user.Id, Totp.Code(secret, Step));

            Assert.Equal(10, fresh!.Count);
            Assert.Empty(fresh.Intersect(oldCodes));
            Assert.Equal(10, _codes.Count);
            Assert.Null(await _service.VerifyChallengeAsync(await _service.CreateChallengeAsync(_user.Id), oldCodes[0]));
            Assert.NotNull(await _service.VerifyChallengeAsync(await _service.CreateChallengeAsync(_user.Id), fresh[0]));
        }

        [Fact]
        public async Task Regenerate_WithInvalidCode_KeepsTheOldCodes()
        {
            var (secret, oldCodes) = await EnableAsync();

            Assert.Null(await _service.RegenerateRecoveryCodesAsync(_user.Id, Totp.Code(secret, Step + 5)));
            Assert.NotNull(await _service.VerifyChallengeAsync(await _service.CreateChallengeAsync(_user.Id), oldCodes[0]));
        }

        [Fact]
        public async Task Reset_TurnsMfaOffWithoutACode()
        {
            await EnableAsync();

            await _service.ResetAsync(_user.Id, AdminId);

            Assert.False(_user.MfaEnabled);
            Assert.Null(_user.MfaSecret);
            Assert.Empty(_codes);
        }

        [Fact]
        public async Task Audit_TurningMfaOn_RecordsWhoAndWhat()
        {
            await EnableAsync();

            var row = LastAudit();
            Assert.Equal(_user.Id, row.UserId);
            Assert.False(Change(row, "MfaEnabled").GetProperty("OldValue").GetBoolean());
            Assert.True(Change(row, "MfaEnabled").GetProperty("NewValue").GetBoolean());
        }

        [Fact]
        public async Task Audit_TurningMfaOff_RecordsTheUserAsActorAndTheReason()
        {
            var (secret, _) = await EnableAsync();

            await _service.DisableAsync(_user.Id, Totp.Code(secret, Step));

            var row = LastAudit();
            Assert.Equal(_user.Id, row.UserId);
            Assert.False(Change(row, "MfaEnabled").GetProperty("NewValue").GetBoolean());
            Assert.Equal("Turned off by the user", Change(row, "Reason").GetProperty("NewValue").GetString());
        }

        [Fact]
        public async Task Audit_AdminReset_RecordsTheAdministratorAsActor()
        {
            await EnableAsync();

            await _service.ResetAsync(_user.Id, AdminId);

            var row = LastAudit();
            Assert.Equal(AdminId, row.UserId);
            Assert.Equal("Reset by an administrator", Change(row, "Reason").GetProperty("NewValue").GetString());
        }

        [Fact]
        public async Task Audit_SpendingARecoveryCode_RecordsHowManyAreLeft()
        {
            var (_, recovery) = await EnableAsync();

            await _service.VerifyChallengeAsync(await _service.CreateChallengeAsync(_user.Id), recovery[0]);

            var row = LastAudit();
            Assert.Equal(10, Change(row, "RecoveryCodesRemaining").GetProperty("OldValue").GetInt32());
            Assert.Equal(9, Change(row, "RecoveryCodesRemaining").GetProperty("NewValue").GetInt32());
        }

        [Fact]
        public async Task Audit_RegeneratingCodes_IsRecorded()
        {
            var (secret, _) = await EnableAsync();

            await _service.RegenerateRecoveryCodesAsync(_user.Id, Totp.Code(secret, Step));

            Assert.Equal("Regenerated", Change(LastAudit(), "RecoveryCodes").GetProperty("NewValue").GetString());
        }

        [Fact]
        public async Task Audit_AFailedCode_RecordsNothing()
        {
            var (secret, _) = await EnableAsync();
            var before = _auditRows.Count;

            await _service.DisableAsync(_user.Id, Totp.Code(secret, Step + 5));
            await _service.VerifyChallengeAsync(await _service.CreateChallengeAsync(_user.Id), "AAAAA-AAAAA");

            Assert.Equal(before, _auditRows.Count);
        }

        [Fact]
        public void Audit_MfaRowsAreListedByTheAuditTrailScreen()
        {
            Assert.Contains(AuditedEntities.Mfa, AuditedEntities.Names);
            Assert.False(AuditedEntities.Contains(typeof(UserEntity)));
        }

        [Fact]
        public async Task AFailedCode_IsLoggedWithTheUserAndTheFailureCount()
        {
            var (secret, _) = await EnableAsync();
            var token = await _service.CreateChallengeAsync(_user.Id);
            var sink = new CollectingSink();
            var previous = Serilog.Log.Logger;
            Serilog.Log.Logger = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
            try
            {
                await _service.VerifyChallengeAsync(token, Totp.Code(secret, Step + 5));
            }
            finally
            {
                Serilog.Log.Logger = previous;
            }

            var rejected = Assert.Single(sink.Events, e => e.MessageTemplate.Text.StartsWith("MFA code rejected"));
            Assert.Equal(_user.Id.ToString(), rejected.Properties["UserId"].ToString());
            Assert.Equal("1", rejected.Properties["Failures"].ToString());
            Assert.DoesNotContain(Totp.Code(secret, Step + 5), rejected.RenderMessage());
        }

        private sealed class CollectingSink : Serilog.Core.ILogEventSink
        {
            public List<Serilog.Events.LogEvent> Events { get; } = new();
            public void Emit(Serilog.Events.LogEvent logEvent)
            {
                lock (Events)
                    Events.Add(logEvent);
            }
        }

        private sealed class FixedTime(DateTimeOffset now) : TimeProvider
        {
            private DateTimeOffset _now = now;
            public void Advance(TimeSpan by) => _now += by;
            public override DateTimeOffset GetUtcNow() => _now;
        }

        private static void EnsureEncryptionKeyInitialized()
        {
            var field = typeof(SharedFunctions).GetField("_encryptionKey", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null || !string.IsNullOrWhiteSpace(field.GetValue(null) as string))
                return;

            field.SetValue(null, Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        }
    }
}
