namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Certificate metadata for the settings screen. Carries no key material and no password —
    /// see EnterpriseFiscalSettingsDtoPrivacyTests for the equivalent guard on the profile DTO.
    /// </summary>
    /// <remarks>
    /// Deliberately carries <b>no emitente identity</b> — no CNPJ, no certificate subject. This is
    /// the one fiscal response readable at Manager (<c>nfe.certificate.read</c>) rather than
    /// admin-only, because operators need the expiry warning without waiting on an admin. The
    /// identity fields live on <c>EnterpriseFiscalSettingsDTO</c> behind <c>nfe.settings.read</c>,
    /// and <see cref="CnpjMatchesFiscalProfile"/> already carries the only operational signal
    /// derived from them without disclosing the values themselves.
    /// </remarks>
    public class NFeCertificateStatusDTO
    {
        public bool IsConfigured { get; set; }

        /// <summary>A hash, so it identifies the certificate without naming the legal entity.</summary>
        public string? Thumbprint { get; set; }

        public DateTime? NotAfterUtc { get; set; }

        public DateTime? UploadedAtUtc { get; set; }

        /// <summary>Negative once expired. Drives the amber/red badge in the UI.</summary>
        public int? DaysUntilExpiry { get; set; }

        public bool IsExpired { get; set; }

        /// <summary>True within 30 days of expiry, so operators can act before issuance stops.</summary>
        public bool IsExpiringSoon { get; set; }

        /// <summary>
        /// True when the certificate's CNPJ differs from the fiscal profile's. SEFAZ rejects
        /// every note in that state, so it is surfaced rather than discovered at issuance.
        /// </summary>
        public bool CnpjMatchesFiscalProfile { get; set; }
    }
}
