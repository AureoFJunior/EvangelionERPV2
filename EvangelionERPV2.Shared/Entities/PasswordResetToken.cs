using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(TokenHash))]
    [Index(nameof(UserId), nameof(IsActive), nameof(ExpiresAt))]
    public class PasswordResetToken : BaseEntity
    {
        [ForeignKey(nameof(User))]
        public Guid UserId { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public int FailedAttempts { get; set; }

        public virtual User? User { get; set; }
    }
}
