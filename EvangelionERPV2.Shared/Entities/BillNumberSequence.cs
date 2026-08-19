using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// Monotonic NossoNumero counter, one row per (enterprise, bank, wallet).
    /// </summary>
    /// <remarks>
    /// A bank assigns a *range* per convênio and requires uniqueness inside it, usually sequential.
    /// The old derivation — the last 8 digits kept from an order GUID plus its timestamp — is
    /// neither: two orders can produce the same 8 digits, and the value lands wherever it lands
    /// inside the range the contract allows. A collision at the bank rejects the title or credits
    /// the wrong one.
    ///
    /// Same shape and same reasoning as <see cref="NFeNumberSequence"/>, deliberately: allocation is
    /// a single atomic <c>UPDATE ... OUTPUT deleted.NextNumber</c>, which is correct across API
    /// instances where an in-process lock is not.
    ///
    /// <see cref="Ceiling"/> is the top of the contracted range, checked inside the same statement
    /// so an exhausted range yields no row instead of a number the bank will not honour. Gaps are
    /// expected and harmless — an unprinted boleto simply burns its number.
    ///
    /// The unique index is NOT filtered on IsActive: a soft-deleted counter that let a replacement
    /// row restart at the range floor would re-issue NossoNumero values already handed to payers.
    /// </remarks>
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
