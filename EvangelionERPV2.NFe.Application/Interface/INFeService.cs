using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Application.Interface
{
    public interface INFeService<TEntity> where TEntity : class
    {
        Task<NFeDocument?> GetByOrderIdAsync(Guid orderId, Guid enterpriseId, NFeDocumentType? type = null);
        /// <param name="contingency">
        /// Set to issue NFC-e off-line (tpEmis 9) when SEFAZ cannot be reached. Null issues normally.
        /// </param>
        Task<NFeDocument?> IssueAsync(
            Guid orderId,
            Guid enterpriseId,
            NFeDocumentType type = NFeDocumentType.NFe,
            NFeContingency? contingency = null);
        Task<NFeDocument?> ConsultAsync(string accessKey, Guid enterpriseId);
        Task<NFeDocument?> CancelAsync(string accessKey, Guid enterpriseId, string reason = "");
    }
}
