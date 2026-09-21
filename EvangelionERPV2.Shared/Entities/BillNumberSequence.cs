using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// Monotonic NossoNumero counter, one row per (enterprise, bank, wallet).
    /// </summary>
    [Table("BillNumberSequence")]
    [Index(nameof(EnterpriseId), nameof(BankCode), nameof(Wallet), IsUnique = true)]
    public class BillNumberSequence : BaseEntity
    {
        /// <summary>FEBRABAN bank code, e.g. 237 Bradesco, 33 Santander.</summary>
        public int BankCode { get; set; }

        [MaxLength(3)]
        public string Wallet { get; set; } = "";

        /// <summary>
        /// Next NossoNumero to hand out. Incremented in place by the allocation statement.
        /// <c>long</c> rather than <c>int</c> because the field runs to 17 digits at some banks.
        /// </summary>
        public long NextNumber { get; set; } = 1;

        /// <summary>Highest number the convênio allows. Allocation stops here rather than wrapping.</summary>
        public long Ceiling { get; set; }

        [ForeignKey(nameof(Enterprise))]
        public Guid EnterpriseId { get; set; }
        public virtual Enterprise? Enterprise { get; set; }
    }
}
