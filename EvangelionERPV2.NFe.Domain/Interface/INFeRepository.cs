using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Domain.Interface
{
    public interface INFeRepository<TEntity> where TEntity : class
    {
        Task<NFeDocument?> GetByOrderIdAsync(Guid orderId, NFeDocumentType? type = null);
        Task<NFeDocument?> GetByAccessKeyAsync(string accessKey);
    }
}
