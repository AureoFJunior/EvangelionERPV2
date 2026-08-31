using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Caching.Memory;

namespace EvangelionERPV2.NFeModule.Infra.Certificates
{
    /// <summary>
    /// Resolves a tenant's Código de Segurança do Contribuinte from AWS Secrets Manager, stored at
    /// <c>{prefix}{enterpriseId}</c> as <c>{"csc": "..."}</c>.
    /// </summary>
    /// <remarks>
    /// The CSC is a signing key, not a setting. The consumer QR on an NFCe is an HMAC over the
    /// access key keyed by this value, so anyone holding it can mint QR codes that verify as the
    /// shop's. It is therefore treated exactly like the A1 certificate: Secrets Manager only, never
    /// a database column, never a DTO field, never a log line, and never readable back through the
    /// API. The till never sees it — a terminal on a shop floor holds a sale and nothing that can
    /// sign for the company.
    ///
    /// Mirrors <see cref="EnterpriseCertificateProvider"/> deliberately rather than inventing a
    /// second mechanism; the `plain:` rejection below is the same reasoning as that type's.
    /// </remarks>
    public sealed class EnterpriseCscProvider : IEnterpriseCscProvider
    {
        internal const string CscKey = "csc";

        /// <summary>
        /// Short enough that a rotation takes effect without operator action, long enough that a
        /// busy counter does not hammer Secrets Manager — the same trade-off the certificate makes.
        /// </summary>
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

        private readonly AWSKMSKeyProvider _kmsKeyProvider;
        private readonly IMemoryCache _cache;
        private readonly string _secretPrefix;

        public EnterpriseCscProvider(
            AWSKMSKeyProvider kmsKeyProvider,
            IMemoryCache cache,
            string secretPrefix)
        {
            _kmsKeyProvider = kmsKeyProvider;
            _cache = cache;
            _secretPrefix = secretPrefix;
        }

        public async Task<string> GetAsync(Guid enterpriseId, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"nfe-csc:{enterpriseId}";
            if (_cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
                return cached;

            var secretId = $"{_secretPrefix}{enterpriseId}";

            // The `plain:` escape hatch in AWSKMSKeyProvider is gated only by ASPNETCORE_ENVIRONMENT.
            // Adequate for a connection string, not for a value that signs consumer documents.
            if (secretId.StartsWith("plain:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A CSC must never be read from a plain: secret; it is a signing key.");

            string csc;
            try
            {
                csc = await _kmsKeyProvider.GetKMSKeyAsync($"{secretId}:{CscKey}", bypassCache: true, cancellationToken);
            }
            catch (ResourceNotFoundException)
            {
                // A tenant that has not been given a CSC yet has no secret at all. Expected state,
                // not a fault — an unconfigured shop must not surface as a 500.
                throw new InvalidOperationException(
                    "No CSC is configured for this enterprise. Set one through UpdateCsc before issuing an NFCe.");
            }

            if (string.IsNullOrWhiteSpace(csc))
                throw new InvalidOperationException("The stored CSC is empty.");

            _cache.Set(cacheKey, csc, CacheDuration);
            return csc;
        }

        public void Invalidate(Guid enterpriseId) => _cache.Remove($"nfe-csc:{enterpriseId}");
    }
}
