using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace EvangelionERPV2.UserModule.Application.Token
{
    public class TokenService
    {
        private const int MaxTokenPersistAttempts = 2;
        private const int RefreshTokenCleanupBatchSize = 200;
        private static readonly TimeSpan RefreshTokenRetentionWindow = TimeSpan.FromDays(30);
        private static readonly TimeSpan RefreshTokenCleanupInterval = TimeSpan.FromMinutes(10);
        private static long _lastRefreshTokenCleanupTicks;
        private readonly IRepository<RefreshToken> _refreshTokenRepository;
        private readonly IConfiguration _configuration;

        public TokenService(IRepository<RefreshToken> refreshTokenRepository, IConfiguration configuration)
        {
            _refreshTokenRepository = refreshTokenRepository;
            _configuration = configuration;
        }

        public string GenerateToken(User user)
        {
            if (user == null || user.Id == Guid.Empty)
                throw new ArgumentException("User with valid Id is required.", nameof(user));

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(SharedFunctions.GetEncryptionKey());
            var userId = user.Id.ToString();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, userId),
                    new Claim(ClaimTypes.GivenName, user.FirstName),
                    new Claim(ClaimTypes.Surname, user.LastName),
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Sid, userId),
                    new Claim("uid", userId),
                    new Claim(ClaimTypes.GroupSid, user?.EnterpriseId?.ToString() ?? string.Empty)
                }),
                Expires = DateTime.UtcNow.AddMinutes(GetAccessTokenMinutes()),
                Issuer = GetIssuer(),
                Audience = GetAudience(),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public string GenerateToken(IEnumerable<Claim> claims)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(SharedFunctions.GetEncryptionKey());
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(GetAccessTokenMinutes()),
                Issuer = GetIssuer(),
                Audience = GetAudience(),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public string GenerateRefreshToken()
        {
            var randomNumber = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        public static ClaimsPrincipal GetPrincipalFromExpiredTokens(string token)
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                ValidateIssuer = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SharedFunctions.GetEncryptionKey())),
                ValidateLifetime = false
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);
            if (securityToken is not JwtSecurityToken jwtSecurityToken || !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                throw new SecurityTokenException("Invalid token");

            return principal;
        }

        public async Task SaveRefreshTokenAsync(Guid userId, string refreshToken)
        {
            if (userId == Guid.Empty || string.IsNullOrWhiteSpace(refreshToken))
                throw new ArgumentException("UserId and refreshToken are required.");

            var now = DateTime.UtcNow;
            var tokenHash = HashToken(refreshToken);

            var existingActiveToken = (await _refreshTokenRepository.GetAllAsyncByFilter(
                descending: false,
                pageNumber: 1,
                pageSize: 1,
                predicate: x => x.UserId == userId && x.TokenHash == tokenHash && x.IsActive == true && x.RevokedAt == null && x.ExpiresAt > now))?.FirstOrDefault();

            if (existingActiveToken != null)
                return;

            await TryCleanupStaleRefreshTokensAsync(now);

            DbUpdateException? lastUniqueViolation = null;
            for (var attempt = 1; attempt <= MaxTokenPersistAttempts; attempt++)
            {
                RefreshToken? insertedToken = null;
                try
                {
                    await _refreshTokenRepository.ExecuteInTransactionAsync(async () =>
                    {
                        var activeTokens = (await _refreshTokenRepository.GetAllAsyncByFilter(
                            descending: false,
                            pageNumber: 1,
                            pageSize: int.MaxValue,
                            predicate: x => x.UserId == userId && x.IsActive == true))
                            .ToList();

                        foreach (var token in activeTokens)
                        {
                            token.RevokedAt ??= now;
                            token.IsActive = false;
                            token.UpdatedAt = now;
                            _refreshTokenRepository.Update(token);
                        }

                        if (activeTokens.Count > 0)
                            await _refreshTokenRepository.CommitAsync();

                        insertedToken = new RefreshToken
                        {
                            UserId = userId,
                            TokenHash = tokenHash,
                            ExpiresAt = now.AddDays(GetRefreshTokenDays()),
                            IsActive = true,
                            CreatedAt = now,
                            UpdatedAt = now
                        };

                        await _refreshTokenRepository.CreateAsync(insertedToken);
                        await _refreshTokenRepository.CommitAsync();
                    });

                    return;
                }
                catch (DbUpdateException ex) when (attempt < MaxTokenPersistAttempts && IsUniqueActiveTokenViolation(ex, "IX_RefreshToken_UserId_Active"))
                {
                    DetachIfTracked(insertedToken);
                    lastUniqueViolation = ex;
                    continue;
                }
                catch (DbUpdateException ex) when (IsUniqueActiveTokenViolation(ex, "IX_RefreshToken_UserId_Active"))
                {
                    DetachIfTracked(insertedToken);
                    lastUniqueViolation = ex;
                    break;
                }
            }

            throw new InvalidOperationException("Unable to persist refresh token after retry attempts.", lastUniqueViolation);
        }

        public async Task<bool> ValidateRefreshTokenAsync(Guid userId, string refreshToken)
        {
            if (userId == Guid.Empty || string.IsNullOrWhiteSpace(refreshToken))
                return false;

            var now = DateTime.UtcNow;
            await TryCleanupStaleRefreshTokensAsync(now);
            var tokenHash = HashToken(refreshToken);
            var tokens = await _refreshTokenRepository.GetAllAsyncByFilter(
                descending: false,
                pageNumber: 1,
                pageSize: 1,
                predicate: x => x.UserId == userId && x.TokenHash == tokenHash && x.IsActive == true && x.RevokedAt == null);

            var token = tokens?.FirstOrDefault();
            return token != null && token.RevokedAt == null && token.ExpiresAt > now;
        }

        public async Task RevokeRefreshTokenAsync(Guid userId, string refreshToken)
        {
            if (userId == Guid.Empty || string.IsNullOrWhiteSpace(refreshToken))
                return;

            var now = DateTime.UtcNow;
            await TryCleanupStaleRefreshTokensAsync(now);

            var tokenHash = HashToken(refreshToken);
            var tokens = await _refreshTokenRepository.GetAllAsyncByFilter(
                descending: false,
                pageNumber: 1,
                pageSize: 1,
                predicate: x => x.UserId == userId && x.TokenHash == tokenHash && x.IsActive == true && x.RevokedAt == null);
            var token = tokens?.FirstOrDefault();
            if (token == null || token.RevokedAt != null)
                return;

            token.RevokedAt = now;
            token.IsActive = false;
            token.UpdatedAt = now;
            _refreshTokenRepository.Update(token);
            await _refreshTokenRepository.CommitAsync();
        }

        private string GetIssuer()
        {
            return _configuration.GetSection("JwtSettings")["Issuer"] ?? string.Empty;
        }

        private string GetAudience()
        {
            return _configuration.GetSection("JwtSettings")["Audience"] ?? string.Empty;
        }

        private int GetAccessTokenMinutes()
        {
            var raw = _configuration.GetSection("JwtSettings")["AccessTokenMinutes"];
            return int.TryParse(raw, out var minutes) && minutes > 0 ? minutes : 60;
        }

        private int GetRefreshTokenDays()
        {
            var raw = _configuration.GetSection("JwtSettings")["RefreshTokenDays"];
            return int.TryParse(raw, out var days) && days > 0 ? days : 7;
        }

        private static string HashToken(string refreshToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
            return Convert.ToBase64String(bytes);
        }

        private async Task TryCleanupStaleRefreshTokensAsync(DateTime now)
        {
            var nowTicks = now.Ticks;
            var lastCleanupTicks = Interlocked.Read(ref _lastRefreshTokenCleanupTicks);
            if (lastCleanupTicks != 0 && nowTicks - lastCleanupTicks < RefreshTokenCleanupInterval.Ticks)
                return;

            if (Interlocked.CompareExchange(ref _lastRefreshTokenCleanupTicks, nowTicks, lastCleanupTicks) != lastCleanupTicks)
                return;

            var retentionCutoff = now.Subtract(RefreshTokenRetentionWindow);

            await _refreshTokenRepository.ExecuteInTransactionAsync(async () =>
            {
                var staleTokens = (await _refreshTokenRepository.GetAllAsyncByFilter(
                    descending: false,
                    pageNumber: 1,
                    pageSize: RefreshTokenCleanupBatchSize,
                    predicate: token =>
                        token.ExpiresAt <= retentionCutoff ||
                        (token.RevokedAt.HasValue && token.RevokedAt.Value <= retentionCutoff) ||
                        (token.IsActive != true && token.UpdatedAt.HasValue && token.UpdatedAt.Value <= retentionCutoff),
                    orderBy: token => token.CreatedAt))
                    .ToList();

                if (staleTokens.Count == 0)
                    return;

                _refreshTokenRepository.DeleteRange(staleTokens);
                await _refreshTokenRepository.CommitAsync();
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

        private void DetachIfTracked(RefreshToken? refreshToken)
        {
            if (refreshToken == null)
                return;

            _refreshTokenRepository.DetachEntity(refreshToken);
        }
    }
}
