using Amazon;
using Amazon.S3;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace EvangelionERPV2.UserModule.Application.Services
{
    public class UserService : IUserService<User>
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<User> _userRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<RefreshToken> _refreshTokenRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<PasswordResetToken> _passwordResetTokenRepository;
        private readonly IConfiguration _configuration;
        private readonly AWSKMSKeyProvider _kmsProvider;
        private IAmazonS3? _s3Client;
        private static readonly TimeSpan PasswordResetTokenTtl = TimeSpan.FromMinutes(15);
        private const int PasswordResetCodeLength = 8;
        private const int PasswordResetMaxFailedAttempts = 5;
        private const string ResetPasswordPolicyMessage =
            "Password must have at least 8 characters, at least 1 number, and at least 1 uppercase letter or special character.";

        public UserService(EvangelionERPV2.Shared.Repositories.IRepository<User> userRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<RefreshToken> refreshTokenRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<PasswordResetToken> passwordResetTokenRepository,
            IConfiguration configuration,
            AWSKMSKeyProvider kmsProvider)
        {
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _passwordResetTokenRepository = passwordResetTokenRepository;
            _configuration = configuration;
            _kmsProvider = kmsProvider;
        }

        public async Task<User> CreateAsync(User user)
        {
            try
            {
                if (user == null)
                    throw new InsertDatabaseException($"{nameof(User)} is null");

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
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex);
            }
        }

        public User Update(User user)
        {
            User existentUser = _userRepository.GetById(user.Id);
            User updatedUser = new User();

            if (existentUser == null)
                throw new NotFoundDatabaseException($"{nameof(User)} was not found in database.");

            if (!string.IsNullOrWhiteSpace(user.Password) && !SharedFunctions.IsPasswordHashFormat(user.Password))
                user.Password = SharedFunctions.HashPassword(user.Password);

            user.ProfilePicture = SharedFunctions.EnsureEncryptedAddress(user.ProfilePicture);
            updatedUser = _userRepository.Update(user);
            _userRepository.Commit();
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
                Log.Logger.Error(exValidate, "Invalid Google IdToken");
                throw new UnauthorizedAccessException("Invalid Google IdToken", exValidate);
            }

            if (payload == null || string.IsNullOrWhiteSpace(payload.Email) ||
                payload.EmailVerified != true)
            {
                Log.Logger.Warning("Google token did not contain a verified email.");
                throw new UnauthorizedAccessException("Email not verified by Google.");
            }

            // Use payload to find/create the user
            var user = _userRepository.GetByCondition(u => u != null && u.Email == payload.Email).FirstOrDefault();

            if (user != null)
            {
                user.IsLogged = 1;
                _userRepository.Update(user);
                _userRepository.Commit();
                return user;
            }

            throw new NotFoundDatabaseException("User not found. Please register before logging in with SSO.");
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
            await s3Client.DeleteItemIfExistsAsync(bucketName, existentUser.ProfilePicture);

            if (string.IsNullOrWhiteSpace(profilePicturePayload))
            {
                user.ProfilePicture = string.Empty;
                user.UpdatedAt = DateTime.UtcNow;
                return Update(user);
            }

            var keyName = $"users/{user.UserName.ClearString().ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            using var content = SharedFunctions.GetMemoryStreamFromBase64Payload(profilePicturePayload);
            await s3Client.CreateItemAsync(bucketName, keyName, content);

            user.ProfilePicture = SharedFunctions.EnsureEncryptedAddress(keyName);
            user.UpdatedAt = DateTime.UtcNow;
            return Update(user);
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
                Log.Logger.Warning("User profile image not found in S3 for key {KeyName}", profilePictureAddress);
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Unable to load user profile image from S3 for key {KeyName}", profilePictureAddress);
                return string.Empty;
            }
        }

        public async Task<string?> CreatePasswordResetTokenAsync(string email)
        {
            var normalizedEmail = (email ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
                return null;

            var user = _userRepository.GetByCondition(x =>
                x.IsActive == true &&
                string.Equals(x.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (user == null)
                return null;

            var rawToken = GenerateSecureToken();
            var tokenHash = ComputeSha256(rawToken);
            var now = DateTime.UtcNow;

            await _passwordResetTokenRepository.ExecuteInTransactionAsync(async () =>
            {
                var activeTokens = await _passwordResetTokenRepository.GetAllAsyncByFilter(
                    descending: false,
                    pageNumber: 1,
                    pageSize: int.MaxValue,
                    predicate: x =>
                        x.UserId == user.Id &&
                        x.IsActive == true &&
                        x.UsedAt == null &&
                        x.ExpiresAt > now);

                foreach (var activeToken in activeTokens ?? Enumerable.Empty<PasswordResetToken>())
                {
                    activeToken.IsActive = false;
                    activeToken.UpdatedAt = now;
                    _passwordResetTokenRepository.Update(activeToken);
                }

                await _passwordResetTokenRepository.CreateAsync(new PasswordResetToken
                {
                    UserId = user.Id,
                    TokenHash = tokenHash,
                    ExpiresAt = now.Add(PasswordResetTokenTtl),
                    UsedAt = null,
                    FailedAttempts = 0,
                    IsActive = true
                });

                await _passwordResetTokenRepository.CommitAsync();
            });

            return rawToken;
        }

        public async Task ResetPasswordAsync(string email, string token, string newPassword)
        {
            var normalizedEmail = (email ?? string.Empty).Trim();
            var normalizedToken = (token ?? string.Empty).Trim();
            var candidatePassword = newPassword ?? string.Empty;

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

            var user = _userRepository.GetByCondition(x =>
                x.IsActive == true &&
                string.Equals(x.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (user == null)
                throw new ArgumentException("Invalid password reset request.");

            var now = DateTime.UtcNow;
            var tokenHash = ComputeSha256(normalizedToken);
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

            if (!TokenHashMatches(tokenHash, tokenEntity.TokenHash))
            {
                await RegisterPasswordResetFailedAttemptAsync(tokenEntity, now);
                throw new ArgumentException("Invalid password reset request.");
            }

            await _userRepository.ExecuteInTransactionAsync(async () =>
            {
                user.Password = SharedFunctions.HashPassword(candidatePassword);
                user.UpdatedAt = now;
                _userRepository.Update(user);

                tokenEntity.UsedAt = now;
                tokenEntity.IsActive = false;
                tokenEntity.UpdatedAt = now;
                _passwordResetTokenRepository.Update(tokenEntity);

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
                    refreshToken.UpdatedAt = now;
                    _refreshTokenRepository.Update(refreshToken);
                }

                await _userRepository.CommitAsync();
            });
        }

        private async Task<IAmazonS3> GetS3ClientAsync()
        {
            if (_s3Client != null)
                return _s3Client;

            var awsCredentials = await _kmsProvider.GetAWSCredentialsAsync();
            _s3Client = new AmazonS3Client(awsCredentials, RegionEndpoint.USEast1);
            return _s3Client;
        }

        private static string GenerateSecureToken()
        {
            var maxExclusive = (int)Math.Pow(10, PasswordResetCodeLength);
            var value = RandomNumberGenerator.GetInt32(0, maxExclusive);
            return value.ToString($"D{PasswordResetCodeLength}");
        }

        private static string ComputeSha256(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input ?? string.Empty));
            return Convert.ToBase64String(bytes);
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

        private static bool TokenHashMatches(string providedTokenHash, string storedTokenHash)
        {
            var providedBytes = Encoding.UTF8.GetBytes(providedTokenHash ?? string.Empty);
            var storedBytes = Encoding.UTF8.GetBytes(storedTokenHash ?? string.Empty);
            return CryptographicOperations.FixedTimeEquals(providedBytes, storedBytes);
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
    }
}
