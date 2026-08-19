using System.Text.RegularExpressions;

namespace EvangelionERPV2.NFeModule.Infra.Certificates
{
    /// <summary>
    /// Non-secret facts about a stored A1 certificate, safe to persist and to return over the API.
    /// Deliberately carries no key material and no password.
    /// </summary>
    /// <param name="Thumbprint">SHA-1 thumbprint, uppercase hex.</param>
    /// <param name="SubjectCn">Subject CN, e.g. "NERV INDUSTRIA LTDA:11222333000181".</param>
    /// <param name="Cnpj">CNPJ digits parsed out of the subject, empty when the CN carries none.</param>
    /// <param name="NotAfterUtc">Expiry. Issuance is refused once this passes.</param>
    /// <param name="UploadedAtUtc">When this certificate was stored.</param>
    public sealed record NFeCertificateMetadata(
        string Thumbprint,
        string SubjectCn,
        string Cnpj,
        DateTime NotAfterUtc,
        DateTime UploadedAtUtc)
    {
        // Brazilian e-CNPJ subjects look like "NERV INDUSTRIA LTDA:11222333000181".
        private static readonly Regex CnpjInSubject = new(@"(?<!\d)(\d{14})(?!\d)", RegexOptions.Compiled);

        /// <summary>
        /// Pulls the 14-digit CNPJ out of a certificate subject, or empty when there is none.
        /// </summary>
        public static string ExtractCnpj(string? subjectCn)
        {
            var match = CnpjInSubject.Match(subjectCn ?? string.Empty);
            return match.Success ? match.Value : string.Empty;
        }

        /// <summary>
        /// How far ahead operators are warned, so a renewal can be bought before issuance stops.
        /// </summary>
        public static readonly TimeSpan ExpiryWarningWindow = TimeSpan.FromDays(30);

        /// <summary>Whole days left, floored; negative once expired.</summary>
        public static int DaysUntilExpiry(DateTime notAfterUtc, DateTime nowUtc) =>
            (int)Math.Floor((notAfterUtc - nowUtc).TotalDays);
    }
}
