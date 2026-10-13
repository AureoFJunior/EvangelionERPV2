using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.NFeModule.Application.Interface
{
    public interface INFeCertificateService
    {
        /// <summary>
        /// Validates and stores the tenant's A1 certificate, then persists its metadata on the
        /// fiscal profile. Rejects an upload whose CNPJ does not match the configured emitente.
        /// </summary>
        Task<NFeCertificateStatusDTO> UploadAsync(
            Guid enterpriseId, byte[] pfxBytes, string password, CancellationToken cancellationToken = default);

        /// <summary>Metadata only; never touches key material.</summary>
        Task<NFeCertificateStatusDTO> GetStatusAsync(Guid enterpriseId, CancellationToken cancellationToken = default);

        /// <summary>Removes the certificate and clears its metadata from the fiscal profile.</summary>
        Task DeleteAsync(Guid enterpriseId, CancellationToken cancellationToken = default);
    }
}
