using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    /// <summary>
    /// One upload of an OFX statement: who it belongs to, what the bank said the balance was, and
    /// how much of the file was actually new.
    /// </summary>
    /// <remarks>
    /// The ledger balance has to be persisted rather than derived. A statement carries no opening
    /// balance, so summing <see cref="BankStatementLine"/> rows produces a movement total, not a
    /// balance — and the variance screen needs the bank's own closing figure to subtract from
    /// <see cref="Enterprise.CurrentBalance"/>.
    ///
    /// Nothing here ever writes back to <see cref="Enterprise.CurrentBalance"/>. The balance stays
    /// app-computed on purpose: this table is the second opinion, not the source of truth.
    /// </remarks>
    [Index(nameof(EnterpriseId), nameof(BalanceAsOf))]
    [Table("BankStatementImport")]
    public class BankStatementImport : BaseEntity
    {
        public Guid EnterpriseId { get; set; }

        /// <summary>OFX <c>BANKID</c> — the bank's clearing code.</summary>
        [MaxLength(32)]
        public string BankId { get; set; } = string.Empty;

        /// <summary>OFX <c>ACCTID</c> — the account number the statement covers.</summary>
        [MaxLength(64)]
        public string AccountId { get; set; } = string.Empty;

        /// <summary>OFX <c>LEDGERBAL/BALAMT</c>: what the bank says the account held.</summary>
        public decimal LedgerBalance { get; set; }

        /// <summary>OFX <c>LEDGERBAL/DTASOF</c>: the date <see cref="LedgerBalance"/> was true.</summary>
        public DateTime BalanceAsOf { get; set; }

        /// <summary>Transactions present in the file.</summary>
        public int LineCount { get; set; }

        /// <summary>
        /// Transactions this import actually inserted. A re-upload of the same file leaves this at
        /// zero, which is the visible proof that the FITID key held.
        /// </summary>
        public int NewLineCount { get; set; }
    }
}
