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

        /// <summary>The app-computed balance next to the bank's, and the gap between them.</summary>
        Task<BankReconciliationVarianceDTO> GetVarianceAsync(Guid enterpriseId);
    }
}
