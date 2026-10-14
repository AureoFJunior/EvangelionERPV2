using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    public class RefreshToken : BaseEntity
    {
        [ForeignKey(nameof(User))]
        public Guid UserId { get; set; }
        public string TokenHash { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? ReplacedByTokenHash { get; set; }

        /// <summary>Which sign-in minted it; only <see cref="RefreshTokenKind.Session"/> may be refreshed.</summary>
        [MaxLength(16)]
        public string Kind { get; set; } = RefreshTokenKind.Session;

        public virtual User? User { get; set; }
    }

    public static class RefreshTokenKind
    {
        /// <summary>An interactive ERP sign-in, after MFA when the account has it.</summary>
        public const string Session = "session";

        /// <summary>A PDV terminal sign-in: password only, till-scoped permissions.</summary>
        public const string Till = "till";

        /// <summary>The self-API machine login.</summary>
        public const string Machine = "machine";

        /// <summary>Rows written before kinds existed. Never refreshable: nobody knows what minted them.</summary>
        public const string Legacy = "legacy";
    }
}
