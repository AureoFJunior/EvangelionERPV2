using Amazon.S3;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace EvangelionERPV2.UserModule.Application.Services
{
    public class UserService : IUserService<User>
    {
        private const int MaxTokenPersistAttempts = 2;
        private const int PasswordResetCleanupBatchSize = 200;
        private const string PasswordResetTokenHashPrefix = "HMAC256:";
        private static readonly TimeSpan PasswordResetRetentionWindow = TimeSpan.FromDays(7);
        private static readonly TimeSpan PasswordResetCleanupInterval = TimeSpan.FromMinutes(10);
        private static long _lastPasswordResetCleanupTicks;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<User> _userRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> _enterpriseRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<RefreshToken> _refreshTokenRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<PasswordResetToken> _passwordResetTokenRepository;
        private readonly AppDbContext? _appDbContext;
        private readonly IConfiguration _configuration;
        private readonly AWSKMSKeyProvider _kmsProvider;
        private IAmazonS3? _s3Client;
        private static readonly TimeSpan PasswordResetTokenTtl = TimeSpan.FromMinutes(15);
        private const int PasswordResetCodeLength = 8;
        private const int PasswordResetMaxFailedAttempts = 5;
        private const string ResetPasswordPolicyMessage =
            "Password must have at least 8 characters, at least 1 number, and at least 1 uppercase letter or special character.";

        public UserService(EvangelionERPV2.Shared.Repositories.IRepository<User> userRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> enterpriseRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<RefreshToken> refreshTokenRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<PasswordResetToken> passwordResetTokenRepository,
            IConfiguration configuration,
            AWSKMSKeyProvider kmsProvider,
            AppDbContext? appDbContext = null)
        {
            _userRepository = userRepository;
            _enterpriseRepository = enterpriseRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _passwordResetTokenRepository = passwordResetTokenRepository;
            _appDbContext = appDbContext;
            _configuration = configuration;
            _kmsProvider = kmsProvider;
        }

        public async Task<User> CreateAsync(User user)
        {
            try
            {
                if (user == null)
                    throw new InsertDatabaseException($"{nameof(User)} is null");

                await EnsureActiveEmailIsGloballyUniqueAsync(user.Email, user.Id);

                user.Id = Guid.NewGuid();
                User includedUser = new User();

                user.Password = SharedFunctions.IsPasswordHashFormat(user.Password)
                    ? user.Password
                    : SharedFunctions.HashPassword(user.Password);
                user.ProfilePicture = SharedFunctions.EnsureEncryptedAddress(user.ProfilePicture);
                includedUser = await _userRepository.CreateAsync(user);
                await _userRepository.CommitAsync();
                return includedUser;
            }
            catch (DbUpdateException ex) when (IsUniqueActiveEmailViolation(ex))
            {
                throw new ArgumentException("Email is already in use.", ex);
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while creating user.", ex);
            }
        }

        public User Update(User user)
        {
            User existentUser = _userRepository.GetById(user.Id);
            User updatedUser = new User();

            if (existentUser == null)
                throw new NotFoundDatabaseException($"{nameof(User)} was not found in database.");

            EnsureActiveEmailIsGloballyUnique(user.Email, user.Id);

            if (!string.IsNullOrWhiteSpace(user.Password) && !SharedFunctions.IsPasswordHashFormat(user.Password))
                user.Password = SharedFunctions.HashPassword(user.Password);
            else if (string.IsNullOrWhiteSpace(user.Password))
                user.Password = existentUser.Password;

            user.ProfilePicture = string.IsNullOrWhiteSpace(user.ProfilePicture)
                ? existentUser.ProfilePicture
                : SharedFunctions.EnsureEncryptedAddress(user.ProfilePicture);
            user.CreatedAt = existentUser.CreatedAt;
            user.IsActive = existentUser.IsActive;
            user.EnterpriseId = user.EnterpriseId.HasValue && user.EnterpriseId.Value != Guid.Empty
                ? user.EnterpriseId
                : existentUser.EnterpriseId;
            try
            {
                updatedUser = _userRepository.Update(user);
                _userRepository.Commit();
            }
            catch (DbUpdateException ex) when (IsUniqueActiveEmailViolation(ex))
            {
                throw new ArgumentException("Email is already in use.", ex);
            }

            return updatedUser;
        }

        public User Delete(Guid id)
        {

            User user = _userRepository.GetById(id);
            User deletedUser = new User();

            if (user == null)
                throw new NotFoundDatabaseException($"{nameof(User)} was not found in database.");

            deletedUser = _userRepository.Delete(user);
            _userRepository.Commit();
            return deletedUser;
        }

        public async Task<User> LoginToSSOAsync(string idToken)
        {
            if (string.IsNullOrWhiteSpace(idToken))
                throw new ArgumentException("idToken is required", nameof(idToken));

            var clientId = _kmsProvider.GetKMSKey(_configuration.GetSection("GoogleSettings")["ClientId"] ?? string.Empty);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                Log.Logger.Error("Google client id is not configured.");
                throw new InvalidOperationException("Google client id is not configured.");
            }

            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                    new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });
            }
            catch (Exception exValidate)
            {
                Log.Logger.Warning("Invalid Google IdToken. ErrorType={ErrorType}", exValidate.GetType().Name);
                throw new UnauthorizedAccessException("Invalid Google IdToken");
            }

            if (payload == null || string.IsNullOrWhiteSpace(payload.Email) ||
                payload.EmailVerified != true)
            {
                Log.Logger.Warning("Google token did not contain a verified email.");
                throw new UnauthorizedAccessException("Email not verified by Google.");
            }

            var normalizedEmail = payload.Email.Trim().ToLowerInvariant();
            var matchingUsers = (await _userRepository.GetAllAsyncByFilter(
                descending: false,
                pageNumber: 1,
                pageSize: 10,
                predicate: u =>
                    u.Email != null &&
                    u.Email.ToLower() == normalizedEmail,
                orderBy: u => u.Id))
                .ToList();

            if (matchingUsers.Count == 0)
                throw new UnauthorizedAccessException("Invalid Google sign-in.");

            var activeUsers = matchingUsers
                .Where(x => x.IsActive == true)
                .ToList();

            if (activeUsers.Count == 1)
            {
                var user = activeUsers[0];

                if (user.IsActive != true)
                {
                    Log.Logger.Warning("Rejected Google login for inactive user. EmailId={EmailId}", GetLogSafeStorageIdentifier(payload.Email));
                    throw new UnauthorizedAccessException("User is inactive.");
                }

                if (!user.EnterpriseId.HasValue || user.EnterpriseId.Value == Guid.Empty || !await IsEnterpriseActiveAsync(user.EnterpriseId.Value))
                {
                    Log.Logger.Warning("Rejected Google login for inactive tenant. EmailId={EmailId}", GetLogSafeStorageIdentifier(payload.Email));
                    throw new UnauthorizedAccessException("Tenant is inactive.");
                }

                user.IsLogged = 1;
                _userRepository.Update(user);
                _userRepository.Commit();
                return user;
            }

            if (activeUsers.Count == 0)
            {
                Log.Logger.Warning("Rejected Google login for inactive user. EmailId={EmailId}", GetLogSafeStorageIdentifier(payload.Email));
                throw new UnauthorizedAccessException("User is inactive.");
            }

            if (activeUsers.Count > 1)
            {
                Log.Logger.Warning("Rejected Google login due to ambiguous active users. EmailId={EmailId}", GetLogSafeStorageIdentifier(payload.Email));
                throw new UnauthorizedAccessException("Invalid account state.");
            }

            throw new UnauthorizedAccessException("Invalid account state.");
        }

        public async Task<User> UpdateProfilePictureAsync(User user, string? profilePicturePayload)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var existentUser = _userRepository.GetById(user.Id);
            if (existentUser == null)
                throw new NotFoundDatabaseException($"{nameof(User)} was not found in database.");

            var bucketName = SharedFunctions.GetUserBucketName(_configuration);
            if (string.IsNullOrWhiteSpace(bucketName))
                throw new InvalidOperationException("AWS bucket name is not configured.");

            var s3Client = await GetS3ClientAsync();
            var oldProfilePictureAddress = existentUser.ProfilePicture;

            if (string.IsNullOrWhiteSpace(profilePicturePayload))
            {
                user.ProfilePicture = string.Empty;
                user.UpdatedAt = DateTime.UtcNow;
                var updatedUser = Update(user);
                await TryDeleteOldProfilePictureAsync(s3Client, bucketName, oldProfilePictureAddress);
                return updatedUser;
            }

            var keyName = SharedFunctions.GenerateStorageObjectKey("users", user.Id);
            using var content = SharedFunctions.GetMemoryStreamFromBase64Payload(profilePicturePayload);
            await s3Client.CreateItemAsync(bucketName, keyName, content);
            try
            {
                user.ProfilePicture = SharedFunctions.EnsureEncryptedAddress(keyName);
                user.UpdatedAt = DateTime.UtcNow;
                var updatedUser = Update(user);
                await TryDeleteOldProfilePictureAsync(s3Client, bucketName, oldProfilePictureAddress);
                return updatedUser;
            }
            catch
            {
                await s3Client.DeleteItemIfExistsAsync(bucketName, keyName);
                throw;
            }
        }

        private static async Task TryDeleteOldProfilePictureAsync(IAmazonS3 s3Client, string bucketName, string? oldProfilePictureAddress)
        {
            try
            {
                await s3Client.DeleteItemIfExistsAsync(bucketName, oldProfilePictureAddress);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(
                    "Unable to delete old profile image from S3 after successful profile update. KeyId={KeyId} ErrorType={ErrorType}",
                    GetLogSafeStorageIdentifier(oldProfilePictureAddress),
                    ex.GetType().Name);
            }
        }

        public async Task<string> GetProfilePictureBase64Async(string? profilePictureAddress)
        {
            try
            {
                string bucketName = SharedFunctions.GetUserBucketName(_configuration);
                if (string.IsNullOrWhiteSpace(bucketName))
                    throw new InvalidOperationException("AWS bucket name is not configured.");

                var s3Client = await GetS3ClientAsync();
                return await s3Client.GetItemBase64Async(bucketName, profilePictureAddress);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.BadRequest)
            {
                Log.Logger.Warning(
                    "User profile image not found in S3 for KeyId={KeyId}",
                    GetLogSafeStorageIdentifier(profilePictureAddress));
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(
                    "Unable to load user profile image from S3 for KeyId={KeyId}. ErrorType={ErrorType}",
                    GetLogSafeStorageIdentifier(profilePictureAddress),
                    ex.GetType().Name);
                return string.Empty;
            }
        }

        public async Task<(string? Token, Guid? EnterpriseId)> CreatePasswordResetTokenContextAsync(string email)
        {
            var normalizedEmail = (email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
                return (null, null);

            var now = DateTime.UtcNow;
            await TryCleanupStalePasswordResetTokensAsync(now);

            var user = await ResolveUniqueActiveUserByEmailAsync(normalizedEmail);

            if (user == null || !user.EnterpriseId.HasValue || user.EnterpriseId.Value == Guid.Empty)
                return (null, null);

            if (!await IsEnterpriseActiveAsync(user.EnterpriseId.Value))
                return (null, null);

            for (var attempt = 1; attempt <= MaxTokenPersistAttempts; attempt++)
            {
                var rawToken = GenerateSecureToken();
                var tokenHash = ComputePasswordResetTokenHash(rawToken);

                try
                {
                    await _passwordResetTokenRepository.ExecuteInTransactionAsync(async () =>
                    {
                        await DeactivateActivePasswordResetTokensForUserAsync(user.Id, now);

                        await _passwordResetTokenRepository.CreateAsync(new PasswordResetToken
                        {
                            UserId = user.Id,
                            TokenHash = tokenHash,
                            ExpiresAt = now.Add(PasswordResetTokenTtl),
                            UsedAt = null,
                            FailedAttempts = 0,
                            IsActive = true,
                            CreatedAt = now,
                            UpdatedAt = now
                        });

                        await _passwordResetTokenRepository.CommitAsync();
                    });

                    return (rawToken, user.EnterpriseId);
                }
                catch (DbUpdateException ex) when (attempt < MaxTokenPersistAttempts && IsUniqueActiveTokenViolation(ex, "IX_PasswordResetToken_UserId_Active"))
                {
                    DetachPendingPasswordResetTokenChanges();
                    continue;
                }
            }

            throw new InsertDatabaseException("Unable to create password reset token.");
        }

        public async Task<string?> CreatePasswordResetTokenAsync(string email)
        {
            var (token, _) = await CreatePasswordResetTokenContextAsync(email);
            return token;
        }

        public async Task ResetPasswordAsync(string email, string token, string newPassword)
        {
            var normalizedEmail = (email ?? string.Empty).Trim();
            var normalizedToken = (token ?? string.Empty).Trim();
            var candidatePassword = newPassword ?? string.Empty;
            var now = DateTime.UtcNow;

            await TryCleanupStalePasswordResetTokensAsync(now);

            if (string.IsNullOrWhiteSpace(normalizedEmail) ||
                string.IsNullOrWhiteSpace(normalizedToken) ||
                string.IsNullOrWhiteSpace(candidatePassword))
            {
                throw new ArgumentException("Invalid password reset request.");
            }

            if (!IsValidResetCode(normalizedToken))
                throw new ArgumentException("Invalid password reset request.");

            if (!IsValidResetPassword(candidatePassword))
                throw new ArgumentException(ResetPasswordPolicyMessage);

            var user = await ResolveUniqueActiveUserByEmailAsync(normalizedEmail);

            if (user == null)
                throw new ArgumentException("Invalid password reset request.");

            var tokenEntity = (await _passwordResetTokenRepository.GetAllAsyncByFilter(
                descending: true,
                pageNumber: 1,
                pageSize: 1,
                predicate: x =>
                    x.UserId == user.Id &&
                    x.IsActive == true &&
                    x.UsedAt == null,
                orderBy: x => x.CreatedAt))
                .FirstOrDefault();

            if (tokenEntity == null)
                throw new ArgumentException("Invalid password reset request.");

            if (tokenEntity.ExpiresAt <= now || tokenEntity.FailedAttempts >= PasswordResetMaxFailedAttempts)
            {
                await DeactivatePasswordResetTokenAsync(tokenEntity, now);
                throw new ArgumentException("Invalid password reset request.");
            }

            if (!PasswordResetTokenHashMatches(normalizedToken, tokenEntity.TokenHash))
            {
                await RegisterPasswordResetFailedAttemptAsync(tokenEntity, now);
                throw new ArgumentException("Invalid password reset request.");
            }

            await _userRepository.ExecuteInTransactionAsync(async () =>
            {
                if (_appDbContext != null)
                {
                    var claimedRows = await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE PasswordResetToken SET UsedAt = {now}, IsActive = 0, UpdatedAt = {now} WHERE Id = {tokenEntity.Id} AND UserId = {user.Id} AND TokenHash = {tokenEntity.TokenHash} AND IsActive = 1 AND UsedAt IS NULL AND ExpiresAt > {now}");

                    if (claimedRows <= 0)
                        throw new ArgumentException("Invalid password reset request.");
                }
                else
                {
                    tokenEntity.UsedAt = now;
                    tokenEntity.IsActive = false;
                    tokenEntity.UpdatedAt = now;
                    _passwordResetTokenRepository.Update(tokenEntity);
                }

                user.Password = SharedFunctions.HashPassword(candidatePassword);
                user.UpdatedAt = now;
                _userRepository.Update(user);

                var activeRefreshTokens = await _refreshTokenRepository.GetAllAsyncByFilter(
                    descending: false,
                    pageNumber: 1,
                    pageSize: int.MaxValue,
                    predicate: x =>
                        x.UserId == user.Id &&
                        x.IsActive == true &&
                        x.RevokedAt == null &&
                        x.ExpiresAt > now);

                foreach (var refreshToken in activeRefreshTokens ?? Enumerable.Empty<RefreshToken>())
                {
                    refreshToken.RevokedAt = now;
                    refreshToken.IsActive = false;
                    refreshToken.UpdatedAt = now;
                    _refreshTokenRepository.Update(refreshToken);
                }

                await _userRepository.CommitAsync();
            });
        }

        private async Task<User?> ResolveUniqueActiveUserByEmailAsync(string normalizedEmail)
        {
            var lowerEmail = (normalizedEmail ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lowerEmail))
                return null;

            var matchingUsers = (await _userRepository.GetAllAsyncByFilter(
                descending: false,
                pageNumber: 1,
                pageSize: 2,
                predicate: x =>
                    x.IsActive == true &&
                    x.Email != null &&
                    x.Email.ToLower() == lowerEmail,
                orderBy: x => x.Id))
                .ToList();

            return matchingUsers.Count == 1 ? matchingUsers[0] : null;
        }

        private async Task EnsureActiveEmailIsGloballyUniqueAsync(string? email, Guid userId)
        {
            var normalizedEmail = NormalizeEmail(email);
            if (string.IsNullOrWhiteSpace(normalizedEmail))
                return;

            var matchingUser = (await _userRepository.GetAllAsyncByFilter(
                descending: false,
                pageNumber: 1,
                pageSize: 1,
                predicate: x =>
                    x.IsActive == true &&
                    x.Id != userId &&
                    x.Email != null &&
                    x.Email.Trim().ToLower() == normalizedEmail,
                orderBy: x => x.Id))
                .FirstOrDefault();

            if (matchingUser != null)
                throw new ArgumentException("Email is already in use.");
        }

        private void EnsureActiveEmailIsGloballyUnique(string? email, Guid userId)
        {
            var normalizedEmail = NormalizeEmail(email);
            if (string.IsNullOrWhiteSpace(normalizedEmail))
                return;

            var matchingUser = _userRepository
                .GetByCondition(x =>
                    x.IsActive == true &&
                    x.Id != userId &&
                    x.Email != null &&
                    x.Email.Trim().ToLowerInvariant() == normalizedEmail)
                .FirstOrDefault();

            if (matchingUser != null)
                throw new ArgumentException("Email is already in use.");
        }

        private static string NormalizeEmail(string? email)
        {
            return (email ?? string.Empty).Trim().ToLowerInvariant();
        }

        private async Task<bool> IsEnterpriseActiveAsync(Guid enterpriseId)
        {
            if (enterpriseId == Guid.Empty)
                return false;

            var enterprise = await _enterpriseRepository.GetByIdAsync(enterpriseId);
            return enterprise != null && enterprise.IsActive == true;
        }

        private async Task<IAmazonS3> GetS3ClientAsync()
        {
            if (_s3Client != null)
                return _s3Client;

            var awsCredentials = await _kmsProvider.GetAWSCredentialsAsync();
            var awsRegion = SharedFunctions.GetAWSRegion(_configuration);
            _s3Client = new AmazonS3Client(awsCredentials, awsRegion);
            return _s3Client;
        }

        private static string GenerateSecureToken()
        {
            var maxExclusive = (int)Math.Pow(10, PasswordResetCodeLength);
            var value = RandomNumberGenerator.GetInt32(0, maxExclusive);
            return value.ToString($"D{PasswordResetCodeLength}");
        }

        private string ComputePasswordResetTokenHash(string input)
        {
            var key = GetPasswordResetHashKey();
            using var hmac = new HMACSHA256(key);
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
            return $"{PasswordResetTokenHashPrefix}{Convert.ToBase64String(bytes)}";
        }

        private static string ComputeLegacyPasswordResetTokenHash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input ?? string.Empty));
            return Convert.ToBase64String(bytes);
        }

        private static string GetLogSafeStorageIdentifier(string? storageObjectKey)
        {
            var normalizedStorageObjectKey = (storageObjectKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedStorageObjectKey))
                return "empty";

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedStorageObjectKey));
            return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
        }

        private static bool IsValidResetPassword(string candidatePassword)
        {
            if (string.IsNullOrWhiteSpace(candidatePassword) || candidatePassword.Length < 8)
                return false;

            var hasDigit = candidatePassword.Any(char.IsDigit);
            var hasUppercase = candidatePassword.Any(char.IsUpper);
            var hasSpecialCharacter = candidatePassword.Any(character => !char.IsLetterOrDigit(character));

            return hasDigit && (hasUppercase || hasSpecialCharacter);
        }

        private static bool IsValidResetCode(string token)
        {
            return !string.IsNullOrWhiteSpace(token) &&
                token.Length == PasswordResetCodeLength &&
                token.All(char.IsDigit);
        }

        private bool PasswordResetTokenHashMatches(string providedToken, string storedTokenHash)
        {
            if (string.IsNullOrWhiteSpace(storedTokenHash))
                return false;

            var normalizedStoredTokenHash = storedTokenHash.Trim();
            var expectedHash = normalizedStoredTokenHash.StartsWith(PasswordResetTokenHashPrefix, StringComparison.Ordinal)
                ? ComputePasswordResetTokenHash(providedToken)
                : ComputeLegacyPasswordResetTokenHash(providedToken);

            return FixedTimeEqualsStrings(expectedHash, normalizedStoredTokenHash);
        }

        private static bool FixedTimeEqualsStrings(string left, string right)
        {
            var leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
            var rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);

            if (leftBytes.Length != rightBytes.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }

        private async Task RegisterPasswordResetFailedAttemptAsync(PasswordResetToken tokenEntity, DateTime now)
        {
            await _passwordResetTokenRepository.ExecuteInTransactionAsync(async () =>
            {
                tokenEntity.FailedAttempts += 1;
                tokenEntity.UpdatedAt = now;

                if (tokenEntity.FailedAttempts >= PasswordResetMaxFailedAttempts)
                    tokenEntity.IsActive = false;

                _passwordResetTokenRepository.Update(tokenEntity);
                await _passwordResetTokenRepository.CommitAsync();
            });
        }

        private async Task DeactivateActivePasswordResetTokensForUserAsync(Guid userId, DateTime now)
        {
            if (_appDbContext != null)
            {
                await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE PasswordResetToken SET IsActive = 0, UpdatedAt = {now} WHERE UserId = {userId} AND IsActive = 1");
                return;
            }

            var activeTokens = await _passwordResetTokenRepository.GetAllAsyncByFilter(
                descending: false,
                pageNumber: 1,
                pageSize: int.MaxValue,
                predicate: x =>
                    x.UserId == userId &&
                    x.IsActive == true);

            var tokensToDeactivate = (activeTokens ?? Enumerable.Empty<PasswordResetToken>()).ToList();
            foreach (var activeToken in tokensToDeactivate)
            {
                activeToken.IsActive = false;
                activeToken.UpdatedAt = now;
                _passwordResetTokenRepository.Update(activeToken);
            }

            if (tokensToDeactivate.Count > 0)
                await _passwordResetTokenRepository.CommitAsync();
        }

        private void DetachPendingPasswordResetTokenChanges()
        {
            if (_appDbContext == null)
                return;

            var entries = _appDbContext.ChangeTracker
                .Entries<PasswordResetToken>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
                .ToList();

            foreach (var entry in entries)
                entry.State = EntityState.Detached;
        }

        private async Task DeactivatePasswordResetTokenAsync(PasswordResetToken tokenEntity, DateTime now)
        {
            await _passwordResetTokenRepository.ExecuteInTransactionAsync(async () =>
            {
                tokenEntity.IsActive = false;
                tokenEntity.UpdatedAt = now;
                _passwordResetTokenRepository.Update(tokenEntity);
                await _passwordResetTokenRepository.CommitAsync();
            });
        }

        private byte[] GetPasswordResetHashKey()
        {
            var secret = SharedFunctions.GetEncryptionKey();
            return SHA256.HashData(Encoding.UTF8.GetBytes($"password-reset:{secret}"));
        }

        private async Task TryCleanupStalePasswordResetTokensAsync(DateTime now)
        {
            var nowTicks = now.Ticks;
            var lastCleanupTicks = Interlocked.Read(ref _lastPasswordResetCleanupTicks);
            if (lastCleanupTicks != 0 && nowTicks - lastCleanupTicks < PasswordResetCleanupInterval.Ticks)
                return;

            if (Interlocked.CompareExchange(ref _lastPasswordResetCleanupTicks, nowTicks, lastCleanupTicks) != lastCleanupTicks)
                return;

            var retentionCutoff = now.Subtract(PasswordResetRetentionWindow);

            await _passwordResetTokenRepository.ExecuteInTransactionAsync(async () =>
            {
                var staleTokens = (await _passwordResetTokenRepository.GetAllAsyncByFilter(
                    descending: false,
                    pageNumber: 1,
                    pageSize: PasswordResetCleanupBatchSize,
                    predicate: token =>
                        token.ExpiresAt <= retentionCutoff ||
                        (token.UsedAt.HasValue && token.UsedAt.Value <= retentionCutoff) ||
                        (token.IsActive != true && token.UpdatedAt.HasValue && token.UpdatedAt.Value <= retentionCutoff),
                    orderBy: token => token.CreatedAt))
                    .ToList();

                if (staleTokens.Count == 0)
                    return;

                _passwordResetTokenRepository.DeleteRange(staleTokens);
                await _passwordResetTokenRepository.CommitAsync();
            });
        }

        private static bool IsUniqueActiveTokenViolation(DbUpdateException ex, string indexName)
        {
            var errorText = ex.GetBaseException().Message ?? ex.Message ?? string.Empty;
            return errorText.Contains(indexName, StringComparison.OrdinalIgnoreCase) ||
                   (errorText.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
                    errorText.Contains("UserId", StringComparison.OrdinalIgnoreCase) &&
                    errorText.Contains("IsActive", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsUniqueActiveEmailViolation(DbUpdateException ex)
        {
            var errorText = ex.GetBaseException().Message ?? ex.Message ?? string.Empty;
            return errorText.Contains("IX_User_NormalizedActiveEmail_Active", StringComparison.OrdinalIgnoreCase) ||
                   (errorText.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
                    errorText.Contains("NormalizedActiveEmail", StringComparison.OrdinalIgnoreCase));
        }
    }
}
