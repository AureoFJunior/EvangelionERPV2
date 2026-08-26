using System.Security.Cryptography;

namespace EvangelionERPV2.Shared.Security
{
    /// <summary>
    /// Creates and checks PBKDF2 verifiers, stored as <c>iterations.salt.hash</c>.
    /// </summary>
    /// <remarks>
    /// Used for the two secrets a till holds: a terminal's enrollment credential, and a
    /// supervisor's override PIN. A PIN verifier is handed to the terminal on purpose, because an
    /// override has to work with the network down — which is only safe because a verifier is not
    /// the secret. The iteration count is what makes six digits expensive to guess offline; it is
    /// stored in the string so it can be raised later without invalidating existing rows.
    /// </remarks>
    public static class Pbkdf2Verifier
    {
        private const int SaltBytes = 16;
        private const int HashBytes = 32;
        private const int DefaultIterations = 210_000;

        public static string Create(string secret)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(secret);

            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var hash = Rfc2898DeriveBytes.Pbkdf2(secret, salt, DefaultIterations, HashAlgorithmName.SHA256, HashBytes);

            return $"{DefaultIterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string? secret, string? verifier)
        {
            if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(verifier))
                return false;

            var parts = verifier.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations <= 0)
                return false;

            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(parts[1]);
                expected = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                // A verifier that will not parse is a corrupt row, not a match.
                return false;
            }

            if (expected.Length == 0)
                return false;

            var actual = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
