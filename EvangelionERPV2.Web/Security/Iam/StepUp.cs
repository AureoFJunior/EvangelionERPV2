using System.Security.Claims;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.JsonWebTokens;
using Serilog;

namespace EvangelionERPV2.Web.Security.Iam
{
    /// <summary>
    /// Proof that the caller typed an MFA code recently, bound to the access token's <c>jti</c> so a proof
    /// made on one token never unlocks another (SPEC-iam-zero-trust §6). Lives only in Redis.
    /// </summary>
    public static class StepUp
    {
        public static readonly TimeSpan SensitiveWindow = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan PrivilegedWindow = TimeSpan.FromMinutes(5);

        public static TimeSpan Window(PermissionTier tier) => tier == PermissionTier.Privileged ? PrivilegedWindow : SensitiveWindow;

        public static string? TokenId(ClaimsPrincipal principal) => principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

        public static string Key(string tokenId) => $"iam:stepup:{tokenId}";

        public static async Task<DateTimeOffset?> VerifiedAtAsync(IDistributedCache cache, ClaimsPrincipal principal)
        {
            if (TokenId(principal) is not { Length: > 0 } tokenId)
                return null;

            try
            {
                var raw = await cache.GetStringAsync(Key(tokenId));
                return DateTimeOffset.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at : null;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("Step-up proof unreadable; asking for the code again. ErrorType={ErrorType}", ex.GetType().Name);
                return null;
            }
        }

        public static Task RecordAsync(IDistributedCache cache, string tokenId, DateTimeOffset at) =>
            cache.SetStringAsync(Key(tokenId), at.ToString("O"),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = SensitiveWindow });

        public static Task EndAsync(IDistributedCache cache, string tokenId) => cache.RemoveAsync(Key(tokenId));

        public static bool IsMachineClient(ClaimsPrincipal principal) =>
            principal.HasClaim(Shared.Security.MachineClaims.ClientType, Shared.Security.MachineClaims.SelfApiValue);
    }
}
