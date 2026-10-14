using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Security.Cryptography;
using System.Text;

namespace EvangelionERPV2.UserModule.Application.Services
{
    public sealed record AppleIdentity(string Subject, string? Email, bool EmailVerified, bool IsPrivateRelay);

    /// <summary>Apple's signing keys could not be fetched and none are cached: no token can be checked.</summary>
    public sealed class AppleKeysUnavailableException(Exception inner)
        : Exception("Apple's signing keys are unavailable.", inner);

    /// <summary>
    /// Checks a Sign in with Apple identity token: Apple's signature (keys from its published JWKS),
    /// issuer, one of our client ids as audience, lifetime, and that the nonce claim is the SHA-256 of
    /// the raw nonce this client generated.
    /// </summary>
    public sealed class AppleIdTokenValidator
    {
        public const string Issuer = "https://appleid.apple.com";
        public const string MetadataAddress = Issuer + "/.well-known/openid-configuration";

        private readonly IConfigurationManager<OpenIdConnectConfiguration> _keys;
        private readonly IReadOnlyCollection<string> _audiences;
        private readonly JsonWebTokenHandler _handler = new();

        public AppleIdTokenValidator(IConfigurationManager<OpenIdConnectConfiguration> keys, IEnumerable<string> audiences)
        {
            _keys = keys;
            _audiences = audiences.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
        }

        public async Task<AppleIdentity?> ValidateAsync(string? identityToken, string? rawNonce)
        {
            if (string.IsNullOrWhiteSpace(identityToken) || string.IsNullOrWhiteSpace(rawNonce) || _audiences.Count == 0)
                return null;

            try
            {
                var result = await ValidateSignedAsync(identityToken);
                if (result.Exception is SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException)
                {
                    _keys.RequestRefresh();
                    result = await ValidateSignedAsync(identityToken);
                }

                if (!result.IsValid)
                {
                    Log.Logger.Warning("Apple identity token rejected. ErrorType={ErrorType}", result.Exception?.GetType().Name);
                    return null;
                }

                var claims = result.Claims;
                if (!NonceMatches(claims.TryGetValue("nonce", out var nonce) ? nonce?.ToString() : null, rawNonce))
                {
                    Log.Logger.Warning("Apple identity token rejected. ErrorType={ErrorType}", "NonceMismatch");
                    return null;
                }

                var subject = claims.TryGetValue("sub", out var sub) ? sub?.ToString() : null;
                if (string.IsNullOrWhiteSpace(subject))
                    return null;

                return new AppleIdentity(
                    subject,
                    claims.TryGetValue("email", out var email) ? email?.ToString() : null,
                    IsTrue(claims, "email_verified"),
                    IsTrue(claims, "is_private_email"));
            }
            catch (AppleKeysUnavailableException ex)
            {
                Log.Logger.Error("Apple signing keys unavailable. ErrorType={ErrorType}", ex.InnerException?.GetType().Name);
                throw;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("Apple identity token could not be checked. ErrorType={ErrorType}", ex.GetType().Name);
                return null;
            }
        }

        private async Task<TokenValidationResult> ValidateSignedAsync(string identityToken)
        {
            OpenIdConnectConfiguration configuration;
            try
            {
                configuration = await _keys.GetConfigurationAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                throw new AppleKeysUnavailableException(ex);
            }

            return await _handler.ValidateTokenAsync(identityToken, new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudiences = _audiences,
                IssuerSigningKeys = configuration.SigningKeys,
                ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            });
        }

        private static bool NonceMatches(string? claimed, string rawNonce)
        {
            if (string.IsNullOrEmpty(claimed))
                return false;

            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawNonce)));
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected),
                Encoding.ASCII.GetBytes(claimed.ToUpperInvariant()));
        }

        private static bool IsTrue(IDictionary<string, object> claims, string name) =>
            claims.TryGetValue(name, out var value) && value is bool b ? b : string.Equals(value?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
    }
}
