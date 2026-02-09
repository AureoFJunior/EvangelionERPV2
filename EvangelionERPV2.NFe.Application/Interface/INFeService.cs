using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Application.Interface
{
    public interface INFeService<TEntity> where TEntity : class
    {
        Task<NFeDocument?> GetByOrderIdAsync(Guid orderId, NFeDocumentType? type = null);
        Task<NFeDocument?> IssueAsync(Guid orderId, NFeDocumentType type);
        Task<NFeDocument?> ConsultAsync(string accessKey);
        Task<NFeDocument?> CancelAsync(string accessKey, string reason);
    }
}
