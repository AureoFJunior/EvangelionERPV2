using System.Text.RegularExpressions;
using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The fiscal profile is the one place where someone would be tempted to persist the A1
    /// certificate or the NFCe CSC token "just to make it work". Both are signing secrets: the
    /// certificate is the company's digital identity, and the CSC lets anyone forge a valid NFCe
    /// QR code for the tenant. They live in AWS Secrets Manager and nowhere else.
    /// </summary>
    public class EnterpriseFiscalSettingsDtoPrivacyTests
    {
        private static readonly Regex SecretLookingProperty = new(
            @"pfx|pkcs12|password|senha|privatekey|private_key|certificatedata|certificatebytes|^csc$|csctoken",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [Fact]
        public void EnterpriseFiscalSettingsDTO_ShouldNotExposeSecretMaterial()
        {
            var offenders = typeof(EnterpriseFiscalSettingsDTO)
                .GetProperties()
                .Where(property => SecretLookingProperty.IsMatch(property.Name))
                .Select(property => property.Name)
                .ToArray();

            Assert.True(
                offenders.Length == 0,
                "EnterpriseFiscalSettingsDTO must never carry certificate or CSC secret material. " +
                $"Offending properties: {string.Join(", ", offenders)}.");
        }

        [Fact]
        public void EnterpriseFiscalSettingsDTO_ShouldExposeCscIdWithoutTheToken()
        {
            // CscId is the public idToken half of the pair and is safe to display; the CSC value
            // it pairs with is write-only and never returned.
            var properties = typeof(EnterpriseFiscalSettingsDTO).GetProperties();

            Assert.Contains(properties, property => property.Name == "CscId");
            Assert.DoesNotContain(properties, property =>
                property.Name.Equals("Csc", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void EnterpriseFiscalSettingsDTO_ShouldExposeCertificateMetadataOnly()
        {
            // Metadata is deliberately readable — operators need to see an upcoming expiry
            // without an admin — but nothing beyond thumbprint, subject and dates.
            var certificateProperties = typeof(EnterpriseFiscalSettingsDTO)
                .GetProperties()
                .Where(property => property.Name.StartsWith("Cert", StringComparison.Ordinal))
                .Select(property => property.Name)
                .OrderBy(name => name)
                .ToArray();

            Assert.Equal(
                ["CertNotAfterUtc", "CertSubjectCn", "CertThumbprint", "CertUploadedAtUtc"],
                certificateProperties);
        }
    }
}
