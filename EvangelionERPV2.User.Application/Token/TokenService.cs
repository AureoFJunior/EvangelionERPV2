using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
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
        private readonly IRepository<RefreshToken> _refreshTokenRepository;
        private readonly IConfiguration _configuration;

        public TokenService(IRepository<RefreshToken> refreshTokenRepository, IConfiguration configuration)
        {
            _refreshTokenRepository = refreshTokenRepository;
            _configuration = configuration;
        }

        public string GenerateToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(SharedFunctions.GetEncryptionKey());
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, $"{user.FirstName}-{user.LastName}-{user.UserName}"),
                    new Claim(ClaimTypes.GivenName, user.FirstName),
                    new Claim(ClaimTypes.Surname, user.LastName),
                    new Claim(ClaimTypes.NameIdentifier, user.UserName),
                    new Claim(ClaimTypes.Sid, user.Id.ToString()),
                    new Claim("uid", user.Id.ToString()),
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
            var key = Encoding.ASCII.GetBytes(SharedFunctions.GetEncryptionKey());
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

            await _refreshTokenRepository.ExecuteInTransactionAsync(async () =>
            {
                var now = DateTime.UtcNow;
                var activeTokens = await _refreshTokenRepository.GetAllAsyncByFilter(
                    descending: false,
                    pageNumber: 1,
                    pageSize: int.MaxValue,
                    predicate: x => x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now);

                foreach (var token in activeTokens ?? Enumerable.Empty<RefreshToken>())
                {
                    token.RevokedAt = now;
                    _refreshTokenRepository.Update(token);
                }

                var refreshTokenEntity = new RefreshToken
                {
                    UserId = userId,
                    TokenHash = HashToken(refreshToken),
                    ExpiresAt = now.AddDays(GetRefreshTokenDays())
                };

                await _refreshTokenRepository.CreateAsync(refreshTokenEntity);
                await _refreshTokenRepository.CommitAsync();
            });
        }

        public async Task<bool> ValidateRefreshTokenAsync(Guid userId, string refreshToken)
        {
            if (userId == Guid.Empty || string.IsNullOrWhiteSpace(refreshToken))
                return false;

            var now = DateTime.UtcNow;
            var tokenHash = HashToken(refreshToken);
            var tokens = await _refreshTokenRepository.GetAllAsyncByFilter(
                descending: false,
                pageNumber: 1,
                pageSize: 1,
                predicate: x => x.UserId == userId && x.TokenHash == tokenHash);

            var token = tokens?.FirstOrDefault();
            return token != null && token.RevokedAt == null && token.ExpiresAt > now;
        }

        public async Task RevokeRefreshTokenAsync(Guid userId, string refreshToken)
        {
            if (userId == Guid.Empty || string.IsNullOrWhiteSpace(refreshToken))
                return;

            var tokenHash = HashToken(refreshToken);
            var tokens = await _refreshTokenRepository.GetAllAsyncByFilter(
                descending: false,
                pageNumber: 1,
                pageSize: 1,
                predicate: x => x.UserId == userId && x.TokenHash == tokenHash);
            var token = tokens?.FirstOrDefault();
            if (token == null || token.RevokedAt != null)
                return;

            token.RevokedAt = DateTime.UtcNow;
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
    }
}
