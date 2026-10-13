using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.NFeModule.Application.Interface
{
    public interface ISupplierNFeImportService
    {
        /// <summary>
        /// Turns a supplier's NF-e XML into a payable. Safe to call twice with the same file.
        /// </summary>
        Task<SupplierNFeImportResultDTO> ImportAsync(Guid enterpriseId, byte[] xml, CancellationToken cancellation = default);
    }
}
