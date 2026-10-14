using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
using Serilog;

using EvangelionERPV2.Shared.Exceptions;

namespace EvangelionERPV2.NFeModule.Infra.Certificates
{
    /// <summary>
    /// Stores each tenant's A1 certificate in AWS Secrets Manager under
    /// <c>{prefix}{enterpriseId}</c> as <c>{"pfxBase64": "...", "password": "..."}</c>.
    /// </summary>
    public sealed class EnterpriseCertificateProvider : IEnterpriseCertificateProvider
    {
        internal const string PfxKey = "pfxBase64";
        internal const string PasswordKey = "password";

        /// <summary>
        /// Short enough that a rotation takes effect without operator action, long enough that
        /// a burst of issuances does not hammer Secrets Manager.
        /// </summary>
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

        private readonly IAmazonSecretsManager _secretsManager;
        private readonly AWSKMSKeyProvider _kmsKeyProvider;
        private readonly IMemoryCache _cache;
        private readonly string _secretPrefix;
        private readonly TimeProvider _timeProvider;

        public EnterpriseCertificateProvider(
            IAmazonSecretsManager secretsManager,
            AWSKMSKeyProvider kmsKeyProvider,
            IMemoryCache cache,
            string secretPrefix,
            TimeProvider? timeProvider = null)
        {
            _secretsManager = secretsManager;
            _kmsKeyProvider = kmsKeyProvider;
            _cache = cache;
            _secretPrefix = secretPrefix;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Returns the tenant's signing certificate. <b>The returned instance is shared and cached —
        /// callers must not dispose it.</b>
        /// </summary>
        public async Task<X509Certificate2> GetAsync(Guid enterpriseId, CancellationToken cancellationToken = default)
        {
            var cacheKey = BuildCacheKey(enterpriseId);
            if (_cache.TryGetValue<X509Certificate2>(cacheKey, out var cached) && cached != null)
            {
                EnsureNotExpired(cached, enterpriseId);
                return cached;
            }

            var secretId = BuildSecretId(enterpriseId);

            if (secretId.StartsWith("plain:", StringComparison.OrdinalIgnoreCase))
                throw NFeCertificateException.PlainSecretsForbidden();

            string pfxBase64;
            string password;
            try
            {
                pfxBase64 = await _kmsKeyProvider.GetKMSKeyAsync($"{secretId}:{PfxKey}", bypassCache: true, cancellationToken);
                password = await _kmsKeyProvider.GetKMSKeyAsync($"{secretId}:{PasswordKey}", bypassCache: true, cancellationToken);
            }
            catch (ResourceNotFoundException)
            {
                throw NFeCertificateException.Missing();
            }

            if (string.IsNullOrWhiteSpace(pfxBase64))
                throw NFeCertificateException.Missing();

            X509Certificate2 certificate;
            try
            {
                certificate = LoadCertificate(Convert.FromBase64String(pfxBase64), password);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                Log.Logger.Error(
                    "Stored NFe certificate could not be loaded. EnterpriseId={EnterpriseId} ErrorType={ErrorType}",
                    enterpriseId, ex.GetType().Name);
                throw NFeCertificateException.Unreadable();
            }

            EnsureNotExpired(certificate, enterpriseId);
            WarnIfExpiringSoon(certificate, enterpriseId);

            _cache.Set(cacheKey, certificate, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            });

            return certificate;
        }

        public NFeCertificateMetadata Inspect(byte[] pfxBytes, string password)
        {
            if (pfxBytes == null || pfxBytes.Length == 0)
                throw NFeCertificateException.InvalidUpload();

            X509Certificate2 certificate;
            try
            {
                certificate = LoadCertificate(pfxBytes, password);
            }
            catch (CryptographicException)
            {
                // Covers both "not a PKCS#12" and "wrong password" — deliberately not
                // distinguished, so the endpoint is not a password oracle.
                throw NFeCertificateException.InvalidUpload();
            }

            using (certificate)
            {
                if (!certificate.HasPrivateKey)
                    throw NFeCertificateException.WithoutPrivateKey();

                var notAfterUtc = certificate.NotAfter.ToUniversalTime();
                if (notAfterUtc <= _timeProvider.GetUtcNow().UtcDateTime)
                    throw NFeCertificateException.AlreadyExpiredUpload();

                var subjectCn = ExtractCommonName(certificate);

                return new NFeCertificateMetadata(
                    Thumbprint: certificate.Thumbprint ?? string.Empty,
                    SubjectCn: subjectCn,
                    Cnpj: NFeCertificateMetadata.ExtractCnpj(subjectCn),
                    NotAfterUtc: notAfterUtc,
                    UploadedAtUtc: _timeProvider.GetUtcNow().UtcDateTime);
            }
        }

        public async Task<NFeCertificateMetadata> StoreAsync(
            Guid enterpriseId, byte[] pfxBytes, string password, CancellationToken cancellationToken = default)
        {
            var metadata = Inspect(pfxBytes, password);

            var secretId = BuildSecretId(enterpriseId);
            if (secretId.StartsWith("plain:", StringComparison.OrdinalIgnoreCase))
                throw NFeCertificateException.PlainSecretsForbidden();

            var payload = new JObject
            {
                [PfxKey] = Convert.ToBase64String(pfxBytes),
                [PasswordKey] = password ?? string.Empty
            }.ToString(Newtonsoft.Json.Formatting.None);

            await PutSecretAsync(secretId, payload, cancellationToken);

            Invalidate(enterpriseId);

            Log.Logger.Information(
                "NFe certificate stored. EnterpriseId={EnterpriseId} Thumbprint={Thumbprint} NotAfterUtc={NotAfterUtc}",
                enterpriseId, metadata.Thumbprint, metadata.NotAfterUtc);

            return metadata;
        }

        public async Task DeleteAsync(Guid enterpriseId, CancellationToken cancellationToken = default)
        {
            var secretId = BuildSecretId(enterpriseId);

            try
            {
                await _secretsManager.DeleteSecretAsync(new DeleteSecretRequest
                {
                    SecretId = secretId,
                    ForceDeleteWithoutRecovery = true
                }, cancellationToken);
            }
            catch (ResourceNotFoundException)
            {
            }

            Invalidate(enterpriseId);

            Log.Logger.Information("NFe certificate deleted. EnterpriseId={EnterpriseId}", enterpriseId);
        }

        public void Invalidate(Guid enterpriseId)
        {
            _cache.Remove(BuildCacheKey(enterpriseId));

            var secretId = BuildSecretId(enterpriseId);
            AWSKMSKeyProvider.InvalidateCachedSecret($"{secretId}:{PfxKey}");
            AWSKMSKeyProvider.InvalidateCachedSecret($"{secretId}:{PasswordKey}");
        }

        private async Task PutSecretAsync(string secretId, string payload, CancellationToken cancellationToken)
        {
            try
            {
                await _secretsManager.PutSecretValueAsync(new PutSecretValueRequest
                {
                    SecretId = secretId,
                    SecretString = payload
                }, cancellationToken);
            }
            catch (ResourceNotFoundException)
            {
                await _secretsManager.CreateSecretAsync(new CreateSecretRequest
                {
                    Name = secretId,
                    SecretString = payload,
                    Description = "NFe A1 certificate (managed by EvangelionERPV2)."
                }, cancellationToken);
            }
        }

        /// <summary>
        /// EphemeralKeySet keeps the key in memory: nothing is written to the user key store,
        /// which matters in a read-only container and avoids leaving material on disk.
        /// X509CertificateLoader replaces the X509Certificate2 byte[] constructor, obsolete
        /// since .NET 9 (SYSLIB0057).
        /// </summary>
        private static X509Certificate2 LoadCertificate(byte[] pfxBytes, string? password) =>
            X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                password,
                X509KeyStorageFlags.EphemeralKeySet);

        private void EnsureNotExpired(X509Certificate2 certificate, Guid enterpriseId)
        {
            if (certificate.NotAfter.ToUniversalTime() > _timeProvider.GetUtcNow().UtcDateTime)
                return;

            Log.Logger.Error(
                "NFe certificate has expired. EnterpriseId={EnterpriseId} NotAfterUtc={NotAfterUtc}",
                enterpriseId, certificate.NotAfter.ToUniversalTime());

            throw NFeCertificateException.Expired();
        }

        private void WarnIfExpiringSoon(X509Certificate2 certificate, Guid enterpriseId)
        {
            var remaining = certificate.NotAfter.ToUniversalTime() - _timeProvider.GetUtcNow().UtcDateTime;
            if (remaining <= NFeCertificateMetadata.ExpiryWarningWindow)
            {
                Log.Logger.Warning(
                    "NFe certificate expires soon. EnterpriseId={EnterpriseId} DaysRemaining={DaysRemaining}",
                    enterpriseId, (int)remaining.TotalDays);
            }
        }

        private string BuildSecretId(Guid enterpriseId) =>
            $"{_secretPrefix}{enterpriseId:D}";

        private static string BuildCacheKey(Guid enterpriseId) =>
            $"nfe:cert:{enterpriseId:N}";

        private static string ExtractCommonName(X509Certificate2 certificate)
        {
            var commonName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            return string.IsNullOrWhiteSpace(commonName) ? certificate.Subject : commonName;
        }
    }
}
