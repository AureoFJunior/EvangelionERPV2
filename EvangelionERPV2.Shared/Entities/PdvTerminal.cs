using EvangelionERPV2.Shared.Security;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
