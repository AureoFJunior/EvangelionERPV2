using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Application.Interface
{
    public interface INFeService<TEntity> where TEntity : class
    {
        Task<NFeDocument?> GetByOrderIdAsync(Guid orderId, Guid enterpriseId, NFeDocumentType? type = null);
        Task<NFeDocument?> IssueAsync(Guid orderId, Guid enterpriseId, NFeDocumentType type = NFeDocumentType.NFe);
        Task<NFeDocument?> ConsultAsync(string accessKey, Guid enterpriseId);
        Task<NFeDocument?> CancelAsync(string accessKey, Guid enterpriseId, string reason = "");
    }
}
