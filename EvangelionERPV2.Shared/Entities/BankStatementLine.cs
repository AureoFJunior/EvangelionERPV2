using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// Why a statement line is or is not reconciled.
    /// </summary>
    public enum BankStatementLineStatus
    {
        /// <summary>Nothing in the ERP has this amount inside the window. A human has to clear it.</summary>
        Unmatched = 1,

        /// <summary>Exactly one candidate. <see cref="BankStatementLine.MatchedReferenceId"/> is filled.</summary>
        Matched = 2,

        /// <summary>
        /// More than one candidate fit. Deliberately not resolved: picking one of two identical
        /// amounts by date proximity would silently reconcile the wrong document, and a wrong match
        /// is worse than no match because it stops looking like work.
        /// </summary>
        Ambiguous = 3,

        /// <summary>
        /// A human decided no ERP document corresponds to this line — a bank fee, a tax debit, a
        /// transfer between the company's own accounts. Cleared from the desk without inventing a
        /// match. Appended, never inserted: the value goes on the wire.
        /// </summary>
        Ignored = 4
    }

    /// <summary>
    /// One transaction from an imported OFX statement.
    /// </summary>
    // (EnterpriseId, BankId, AccountId, FitId) is the idempotency key: FITID is unique per account
    // at the bank, not globally, so the account has to be in the key or two banks reusing a short
    // account number would collide. Deliberately NOT filtered on IsActive — a soft-deleted line
    // that let a re-import reinsert the same transaction would double-count it, which is the exact
    // failure this key exists to prevent.
    [Index(nameof(EnterpriseId), nameof(BankId), nameof(AccountId), nameof(FitId), IsUnique = true)]
    // ponytail: one index. The unmatched listing scans it on EnterpriseId and filters Status in
    // memory of the tenant's rows — fine at a statement a day. Add (EnterpriseId, Status) if a
    // tenant ever imports years of history in one go.
    [Table("BankStatementLine")]
    public class BankStatementLine : BaseEntity
    {
        public Guid EnterpriseId { get; set; }

        /// <summary>The upload that first introduced this line. Provenance for an audit question.</summary>
        [ForeignKey(nameof(Import))]
        public Guid ImportId { get; set; }

        public virtual BankStatementImport? Import { get; set; }

        [MaxLength(32)]
        public string BankId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string AccountId { get; set; } = string.Empty;

        /// <summary>OFX <c>FITID</c> — the bank's own id for the transaction.</summary>
        [MaxLength(255)]
        public string FitId { get; set; } = string.Empty;

        /// <summary>
        /// OFX <c>DTPOSTED</c>, truncated to the date. See OfxStatementParser: the clock time
        /// carries no signal for a ±3-day match and its timezone suffix is not worth guessing at.
        /// </summary>
        public DateTime PostedAt { get; set; }

        /// <summary>
        /// Signed as the bank sends it: negative left the account, positive arrived.
        /// decimal(18,2) by the context convention — never double, a cent lost here is a
        /// reconciliation that never closes.
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>OFX <c>TRNTYPE</c> (DEBIT, CREDIT, PIX, …). Kept verbatim for the human reading the list.</summary>
        [MaxLength(32)]
        public string TrnType { get; set; } = string.Empty;

        /// <summary>OFX <c>MEMO</c> or <c>NAME</c> — the counterparty description the bank printed.</summary>
        [MaxLength(512)]
        public string Memo { get; set; } = string.Empty;

        /// <summary><see cref="BankStatementLineStatus"/>.</summary>
        public int Status { get; set; } = (int)BankStatementLineStatus.Unmatched;

        /// <summary>
        /// The Order (credit) or PayableBill (debit) this line was matched to, null while it is
        /// unmatched or ambiguous. Intentionally not a foreign key: the target table differs by
        /// which side of the statement the line sits on, the same reason StockMovement.ReferenceId
        /// is a bare Guid.
        /// </summary>
        public Guid? MatchedReferenceId { get; set; }
    }
}
