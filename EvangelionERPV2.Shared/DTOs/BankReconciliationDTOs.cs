using Microsoft.AspNetCore.Http;

namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Multipart body for the OFX upload. A single <c>[FromForm]</c> model rather than a loose
    /// <c>IFormFile</c> parameter, for the same reason as UploadNFeCertificateRequestDTO:
    /// Swashbuckle cannot map the loose form and takes the whole swagger document down with it.
    /// </summary>
    public class ImportBankStatementRequestDTO
    {
        public IFormFile? File { get; set; }
    }

    /// <summary>What one upload did. <see cref="NewLines"/> is zero on a re-import of the same file.</summary>
    public class BankStatementImportResultDTO
    {
        public Guid ImportId { get; set; }
        public string BankId { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public decimal LedgerBalance { get; set; }
        public DateTime BalanceAsOf { get; set; }
        public int TotalLines { get; set; }
        public int NewLines { get; set; }
        public int MatchedLines { get; set; }
        public int UnmatchedLines { get; set; }
        public int AmbiguousLines { get; set; }
    }

    public class BankStatementLineDTO
    {
        public Guid Id { get; set; }
        public string FitId { get; set; } = string.Empty;
        public DateTime PostedAt { get; set; }
        public decimal Amount { get; set; }
        public string TrnType { get; set; } = string.Empty;
        public string Memo { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Guid? MatchedReferenceId { get; set; }
    }

    /// <summary>
    /// The reconciliation screen. <see cref="CurrentBalance"/> stays the app-computed number and
    /// <see cref="StatementBalance"/> is the bank's — the point is the difference, not replacing
    /// one with the other.
    /// </summary>
    public class BankReconciliationVarianceDTO
    {
        public decimal CurrentBalance { get; set; }

        /// <summary>Null until a statement has been imported: there is nothing to compare against yet.</summary>
        public decimal? StatementBalance { get; set; }

        /// <summary>CurrentBalance minus StatementBalance. Positive means the ERP thinks it holds more than the bank does.</summary>
        public decimal? Variance { get; set; }

        public DateTime? StatementAsOf { get; set; }
        public string? BankId { get; set; }
        public string? AccountId { get; set; }

        /// <summary>Lines still on a human's desk — unmatched plus ambiguous.</summary>
        public int UnclearedLines { get; set; }
    }
}
