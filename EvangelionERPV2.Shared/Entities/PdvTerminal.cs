using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One enrolled till, and the credential that identifies it.
    /// </summary>
    /// <remarks>
    /// The credential authorises nothing. It says only "this caller is a terminal we enrolled",
    /// which is what lets the till reach a login endpoint with no reCAPTCHA — a desktop terminal
    /// has no browser and cannot solve one. Every permission still comes from the operator's own
    /// token.
    ///
    /// Per terminal, never one shared secret: a credential sitting on a shop-floor machine should
    /// be assumed to leak eventually, and a leak must cost one till rather than the whole estate.
    /// That is what <see cref="IsRevoked"/> is for.
    /// </remarks>
    [Index(nameof(EnterpriseId), nameof(TerminalId), IsUnique = true)]
    [Table("PdvTerminal")]
    public class PdvTerminal : BaseEntity
    {
        private const int SaltBytes = 16;
        private const int HashBytes = 32;
        private const int Iterations = 210_000;

        public Guid EnterpriseId { get; set; }

        [MaxLength(64)]
        public string TerminalId { get; set; } = string.Empty;

        /// <summary>
        /// PBKDF2 verifier, stored as <c>iterations.salt.hash</c>. The credential itself is shown
        /// once at enrollment and never persisted — losing it means re-enrolling the terminal,
        /// which is the correct cost.
        /// </summary>
        [MaxLength(256)]
        public string CredentialVerifier { get; set; } = string.Empty;

        public bool IsRevoked { get; set; }

        /// <summary>Last time this terminal successfully authenticated. Useful for spotting a till nobody retired.</summary>
        public DateTime? LastSeenAt { get; set; }

        public static PdvTerminal Enroll(Guid enterpriseId, string terminalId, string credential)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credential);

            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var hash = Rfc2898DeriveBytes.Pbkdf2(credential, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);

            return new PdvTerminal
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                TerminalId = terminalId,
                CredentialVerifier = $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}"
            };
        }

        public bool Verify(string? credential)
        {
            if (IsRevoked || string.IsNullOrEmpty(credential) || string.IsNullOrEmpty(CredentialVerifier))
                return false;

            var parts = CredentialVerifier.Split('.');
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

            var actual = Rfc2898DeriveBytes.Pbkdf2(credential, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
