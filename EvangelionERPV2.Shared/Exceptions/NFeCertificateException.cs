namespace EvangelionERPV2.Shared.Exceptions
{
    /// <summary>
    /// Certificate lifecycle failure. Messages are static and non-sensitive: they must never
    /// echo a password, key material, or an AWS error body.
    /// </summary>
    public class NFeCertificateException : Exception
    {
        public NFeCertificateException(string message)
            : base(message)
        {
        }

        public NFeCertificateException(string message, Exception inner)
            : base(message, inner)
        {
        }

        public static NFeCertificateException Missing() =>
            new("No digital certificate is configured for this enterprise.");

        public static NFeCertificateException Unreadable() =>
            new("The stored digital certificate could not be read. Upload it again.");

        public static NFeCertificateException Expired() =>
            new("The digital certificate has expired. Upload a renewed certificate.");

        public static NFeCertificateException InvalidUpload() =>
            new("The file is not a valid PKCS#12 certificate, or the password is wrong.");

        public static NFeCertificateException WithoutPrivateKey() =>
            new("The certificate has no usable private key and cannot sign fiscal documents.");

        public static NFeCertificateException AlreadyExpiredUpload() =>
            new("The certificate has already expired and cannot be stored.");

        public static NFeCertificateException PlainSecretsForbidden() =>
            new("Plain-text certificate secrets are not allowed in any environment.");
    }
}
