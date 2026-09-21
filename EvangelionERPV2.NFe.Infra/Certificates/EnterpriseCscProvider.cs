using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Newtonsoft.Json.Linq;
using Serilog;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Caching.Memory;

namespace EvangelionERPV2.NFeModule.Infra.Certificates
{
    /// <summary>
    /// Resolves a tenant's Código de Segurança do Contribuinte from AWS Secrets Manager, stored at
    /// <c>{prefix}{enterpriseId}</c> as <c>{"csc": "..."}</c>.
    /// </summary>
    public sealed class EnterpriseCscProvider : IEnterpriseCscProvider
    {
        internal const string CscKey = "csc";

        /// <summary>
        /// Short enough that a rotation takes effect without operator action, long enough that a
        /// busy counter does not hammer Secrets Manager — the same trade-off the certificate makes.
        /// </summary>
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

        private readonly IAmazonSecretsManager _secretsManager;
        private readonly AWSKMSKeyProvider _kmsKeyProvider;
        private readonly IMemoryCache _cache;
        private readonly string _secretPrefix;

        public EnterpriseCscProvider(
            IAmazonSecretsManager secretsManager,
            AWSKMSKeyProvider kmsKeyProvider,
            IMemoryCache cache,
            string secretPrefix)
        {
            _secretsManager = secretsManager;
            _kmsKeyProvider = kmsKeyProvider;
            _cache = cache;
            _secretPrefix = secretPrefix;
        }

        /// <inheritdoc />
        public async Task StoreAsync(Guid enterpriseId, string csc, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(csc))
                throw new InvalidOperationException("A CSC cannot be empty.");

            var secretId = $"{_secretPrefix}{enterpriseId}";
            if (secretId.StartsWith("plain:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A CSC must never be written to a plain: secret; it is a signing key.");

            var payload = new JObject { [CscKey] = csc.Trim() }.ToString(Newtonsoft.Json.Formatting.None);

            try
            {
                await _secretsManager.PutSecretValueAsync(
                    new PutSecretValueRequest { SecretId = secretId, SecretString = payload }, cancellationToken);
            }
            catch (ResourceNotFoundException)
            {
                await _secretsManager.CreateSecretAsync(new CreateSecretRequest
                {
                    Name = secretId,
                    SecretString = payload,
                    Description = "NFCe CSC (managed by EvangelionERPV2)."
                }, cancellationToken);
            }

            Invalidate(enterpriseId);

            Log.Logger.Information("NFCe CSC stored. EnterpriseId={EnterpriseId}", enterpriseId);
        }

        public async Task<string> GetAsync(Guid enterpriseId, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"nfe-csc:{enterpriseId}";
            if (_cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
                return cached;

            var secretId = $"{_secretPrefix}{enterpriseId}";

            if (secretId.StartsWith("plain:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A CSC must never be read from a plain: secret; it is a signing key.");

            string csc;
            try
            {
                csc = await _kmsKeyProvider.GetKMSKeyAsync($"{secretId}:{CscKey}", bypassCache: true, cancellationToken);
            }
            catch (ResourceNotFoundException)
            {
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
