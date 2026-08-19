using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Infra.Certificates;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.NFeModule.Application.Services
{
    /// <summary>
    /// Certificate lifecycle for the admin settings screen. Delegates all key handling to
    /// <see cref="IEnterpriseCertificateProvider"/> and only ever persists metadata.
    /// </summary>
    public class NFeCertificateService : INFeCertificateService
    {
        /// <summary>A real A1 .pfx is 3-8 KB. This is ~30x headroom and still a hard stop.</summary>
        public const int MaxCertificateBytes = 256 * 1024;

        private readonly IEnterpriseCertificateProvider _certificateProvider;
        private readonly IRepository<EnterpriseFiscalSettings> _settingsRepository;
        private readonly TimeProvider _timeProvider;

        public NFeCertificateService(
            IEnterpriseCertificateProvider certificateProvider,
            IRepository<EnterpriseFiscalSettings> settingsRepository,
            TimeProvider? timeProvider = null)
        {
            _certificateProvider = certificateProvider;
            _settingsRepository = settingsRepository;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public async Task<NFeCertificateStatusDTO> UploadAsync(
            Guid enterpriseId, byte[] pfxBytes, string password, CancellationToken cancellationToken = default)
        {
            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Certificate enterprise is required.");

            if (pfxBytes == null || pfxBytes.Length == 0)
                throw new InsertDatabaseException("Certificate file is required.");

            if (pfxBytes.Length > MaxCertificateBytes)
                throw new InsertDatabaseException("Certificate file is too large.");

            // The profile must exist first: without a configured CNPJ there is nothing to match
            // the certificate against, and a mismatch is only discovered when SEFAZ rejects.
            var settings = await RequireSettingsAsync(enterpriseId);

            var metadata = _certificateProvider.Inspect(pfxBytes, password);

            if (!string.IsNullOrEmpty(metadata.Cnpj) &&
                !string.Equals(metadata.Cnpj, settings.Cnpj, StringComparison.Ordinal))
            {
                // Not logged with either CNPJ — both identify a legal entity.
                throw new InsertDatabaseException(
                    "The certificate belongs to a different CNPJ than the configured fiscal profile.");
            }

            var stored = await _certificateProvider.StoreAsync(enterpriseId, pfxBytes, password, cancellationToken);

            settings.CertThumbprint = stored.Thumbprint;
            settings.CertSubjectCn = stored.SubjectCn;
            settings.CertNotAfterUtc = stored.NotAfterUtc;
            settings.CertUploadedAtUtc = stored.UploadedAtUtc;
            settings.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

            _settingsRepository.Update(settings);
            await _settingsRepository.CommitAsync(cancellationToken);

            return BuildStatus(settings);
        }

        public async Task<NFeCertificateStatusDTO> GetStatusAsync(
            Guid enterpriseId, CancellationToken cancellationToken = default)
        {
            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Certificate enterprise is required.");

            var settings = await FindSettingsAsync(enterpriseId);

            return settings == null
                ? new NFeCertificateStatusDTO { IsConfigured = false }
                : BuildStatus(settings);
        }

        public async Task DeleteAsync(Guid enterpriseId, CancellationToken cancellationToken = default)
        {
            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Certificate enterprise is required.");

            await _certificateProvider.DeleteAsync(enterpriseId, cancellationToken);

            var settings = await FindSettingsAsync(enterpriseId);
            if (settings == null)
                return;

            settings.CertThumbprint = null;
            settings.CertSubjectCn = null;
            settings.CertNotAfterUtc = null;
            settings.CertUploadedAtUtc = null;

            // Removing the signing identity must stop issuance — leaving the tenant enabled
            // would fail every note at transmission time instead of saying why here.
            settings.Enabled = false;
            settings.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

            _settingsRepository.Update(settings);
            await _settingsRepository.CommitAsync(cancellationToken);
        }

        private NFeCertificateStatusDTO BuildStatus(EnterpriseFiscalSettings settings)
        {
            if (settings.CertNotAfterUtc == null || string.IsNullOrWhiteSpace(settings.CertThumbprint))
                return new NFeCertificateStatusDTO { IsConfigured = false };

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var notAfterUtc = settings.CertNotAfterUtc.Value;
            var daysUntilExpiry = NFeCertificateMetadata.DaysUntilExpiry(notAfterUtc, nowUtc);
            var certificateCnpj = NFeCertificateMetadata.ExtractCnpj(settings.CertSubjectCn ?? string.Empty);

            return new NFeCertificateStatusDTO
            {
                IsConfigured = true,
                Thumbprint = settings.CertThumbprint,
                NotAfterUtc = notAfterUtc,
                UploadedAtUtc = settings.CertUploadedAtUtc,
                DaysUntilExpiry = daysUntilExpiry,
                IsExpired = notAfterUtc <= nowUtc,
                IsExpiringSoon = notAfterUtc > nowUtc && notAfterUtc - nowUtc <= NFeCertificateMetadata.ExpiryWarningWindow,
                CnpjMatchesFiscalProfile =
                    string.IsNullOrEmpty(certificateCnpj) ||
                    string.Equals(certificateCnpj, settings.Cnpj, StringComparison.Ordinal)
            };
        }

        private async Task<EnterpriseFiscalSettings> RequireSettingsAsync(Guid enterpriseId)
        {
            return await FindSettingsAsync(enterpriseId)
                   ?? throw new InsertDatabaseException(
                       "Configure the fiscal profile before uploading a certificate.");
        }

        private async Task<EnterpriseFiscalSettings?> FindSettingsAsync(Guid enterpriseId)
        {
            return (await _settingsRepository.GetAllAsync(
                    x => x.IsActive == true && x.EnterpriseId == enterpriseId))
                .FirstOrDefault();
        }
    }
}
