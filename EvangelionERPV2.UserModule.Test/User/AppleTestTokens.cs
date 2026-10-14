using EvangelionERPV2.UserModule.Application.Services;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

namespace EvangelionERPV2.UserModule.Test.User
{
    /// <summary>A stand-in for Apple: its signing key, its JWKS endpoint and the tokens it would mint.</summary>
    internal sealed class AppleTestTokens
    {
        public const string RawNonce = "raw-nonce-0123456789abcdef0123456789";
        public static readonly string[] Audiences = { "com.nerv.erp", "com.nerv.erp.web" };

        public RsaSecurityKey Key { get; } = NewKey("apple-key-1");
        public FakeKeys Keys { get; }

        public AppleTestTokens() => Keys = new FakeKeys(Key);

        public AppleIdTokenValidator Validator() => new(Keys, Audiences);

        public static RsaSecurityKey NewKey(string kid) => new(RSA.Create(2048)) { KeyId = kid };

        public static string Sha256Hex(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        public string Token(
            string subject = "001234.abcdef.0001",
            string? email = "ana@example.com",
            object? emailVerified = null,
            string issuer = AppleIdTokenValidator.Issuer,
            string audience = "com.nerv.erp",
            string? nonce = null,
            DateTime? expires = null,
            SecurityKey? key = null,
            IDictionary<string, object>? extra = null)
        {
            var claims = new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["email_verified"] = emailVerified ?? "true",
                ["nonce"] = nonce ?? Sha256Hex(RawNonce)
            };
            if (email != null)
                claims["email"] = email;
            foreach (var pair in extra ?? new Dictionary<string, object>())
                claims[pair.Key] = pair.Value;

            var expiry = expires ?? DateTime.UtcNow.AddMinutes(10);
            var issuedAt = expiry.AddMinutes(-20);
            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                Claims = claims,
                IssuedAt = issuedAt,
                NotBefore = issuedAt,
                Expires = expiry,
                SigningCredentials = new SigningCredentials(key ?? Key, SecurityAlgorithms.RsaSha256)
            });
        }

        internal sealed class FakeKeys : IConfigurationManager<OpenIdConnectConfiguration>
        {
            private SecurityKey[] _current;
            public SecurityKey[]? NextKeys { get; set; }
            public int Refreshes { get; private set; }
            public bool Fail { get; set; }

            public FakeKeys(params SecurityKey[] keys) => _current = keys;

            public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
            {
                if (Fail)
                    throw new InvalidOperationException("apple unreachable");

                var config = new OpenIdConnectConfiguration { Issuer = AppleIdTokenValidator.Issuer };
                foreach (var key in _current)
                    config.SigningKeys.Add(key);
                return Task.FromResult(config);
            }

            public void RequestRefresh()
            {
                Refreshes++;
                if (NextKeys != null)
                    _current = NextKeys;
            }
        }
    }
}
