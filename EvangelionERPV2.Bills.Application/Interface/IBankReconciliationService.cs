using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.BillsModule.Application.Interface
{
    public interface IBankReconciliationService
    {
        /// <summary>
        /// Parses an OFX statement, stores the lines it has not seen before, and matches what it
        /// confidently can. Safe to call twice with the same file.
        /// </summary>
        /// <exception cref="FormatException">The file is not a statement this can read.</exception>
        Task<BankStatementImportResultDTO> ImportAsync(Guid enterpriseId, byte[] content, CancellationToken cancellationToken = default);

        /// <summary>Lines a human still has to clear: no candidate, or more than one.</summary>
        Task<IEnumerable<BankStatementLineDTO>> GetUnclearedAsync(Guid enterpriseId, int? pageNumber = null, int? pageSize = null);

        /// <summary>
        /// Clears one line a human judged: <paramref name="matchedReferenceId"/> names the Order
        /// (money in) or PayableBill (money out) it settles, or null marks it as corresponding to
        /// no ERP document at all — a fee, a tax, a transfer between the company's own accounts.
        /// </summary>
        /// <exception cref="Shared.Exceptions.NotFoundDatabaseException">The line or the document does not exist here.</exception>
        /// <exception cref="Shared.Exceptions.InsertDatabaseException">The document is already reconciled against another line.</exception>
        Task<BankStatementLineDTO> ResolveAsync(Guid enterpriseId, Guid lineId, Guid? matchedReferenceId, CancellationToken cancellationToken = default);

        /// <summary>The app-computed balance next to the bank's, and the gap between them.</summary>
        Task<BankReconciliationVarianceDTO> GetVarianceAsync(Guid enterpriseId);
    }
}
