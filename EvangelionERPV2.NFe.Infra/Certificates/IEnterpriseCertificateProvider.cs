using System.Security.Cryptography.X509Certificates;

using EvangelionERPV2.Shared.Exceptions;

namespace EvangelionERPV2.NFeModule.Infra.Certificates
{
    /// <summary>
    /// Owns the per-tenant A1 certificate. The .pfx bytes and its password live only in AWS
    /// Secrets Manager — never in the database, on disk, in a log, or in an API response.
    /// </summary>
    public interface IEnterpriseCertificateProvider
    {
        /// <summary>
        /// Materialises the tenant's signing certificate.
        /// </summary>
        /// <exception cref="NFeCertificateException">
        /// No certificate stored, the stored material is unreadable, or the certificate has expired.
        /// </exception>
        Task<X509Certificate2> GetAsync(Guid enterpriseId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Parses and validates an uploaded .pfx <b>without storing it</b>, returning metadata only.
        /// </summary>
        NFeCertificateMetadata Inspect(byte[] pfxBytes, string password);

        /// <summary>
        /// Validates then writes the certificate to Secrets Manager, replacing any previous one,
        /// and invalidates every cache layer. Returns the metadata to persist on the fiscal profile.
        /// </summary>
        Task<NFeCertificateMetadata> StoreAsync(
            Guid enterpriseId, byte[] pfxBytes, string password, CancellationToken cancellationToken = default);

        /// <summary>Removes the stored certificate and invalidates caches. Idempotent.</summary>
        Task DeleteAsync(Guid enterpriseId, CancellationToken cancellationToken = default);

        /// <summary>Drops cached material for a tenant without touching Secrets Manager.</summary>
        void Invalidate(Guid enterpriseId);
    }
}
