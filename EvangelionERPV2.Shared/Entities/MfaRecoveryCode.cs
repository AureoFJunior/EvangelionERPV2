using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(UserId))]
    public class MfaRecoveryCode : BaseEntity
    {
        [ForeignKey(nameof(User))]
        public Guid UserId { get; set; }

        [MaxLength(64)]
        public string CodeHash { get; set; } = string.Empty;
        public DateTime? UsedAt { get; set; }

        public virtual User? User { get; set; }
    }
}
