using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One permission held by one user, until <see cref="RevokedAt"/> or <see cref="ExpiresAt"/>. Rows are
    /// never deleted: an ended grant is its own history.
    /// </summary>
    [Table("UserPermissionGrant")]
    public class UserPermissionGrant : BaseEntity
    {
        public Guid EnterpriseId { get; set; }
        public Guid UserId { get; set; }
        public User? User { get; set; }

        [MaxLength(64)]
        public string Permission { get; set; } = string.Empty;

        /// <summary>Allow adds the permission; Deny removes it from what the user's role gives (role mode only).</summary>
        public EnumGrantEffect Effect { get; set; } = EnumGrantEffect.Allow;

        public DateTime? ExpiresAt { get; set; }

        [MaxLength(280)]
        public string? Reason { get; set; }

        public Guid GrantedByUserId { get; set; }
        public DateTime? RevokedAt { get; set; }
        public Guid? RevokedByUserId { get; set; }
        public DateTime? LastUsedAt { get; set; }
    }
}
