using EvangelionERPV2.Shared.Security;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One enrolled till, and the credential that identifies it.
    /// </summary>
    [Index(nameof(EnterpriseId), nameof(TerminalId), IsUnique = true)]
    [Table("PdvTerminal")]
    public class PdvTerminal : BaseEntity
    {
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
            return new PdvTerminal
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                TerminalId = terminalId,
                CredentialVerifier = Pbkdf2Verifier.Create(credential)
            };
        }

        public bool Verify(string? credential) =>
            !IsRevoked && Pbkdf2Verifier.Verify(credential, CredentialVerifier);
    }
}
