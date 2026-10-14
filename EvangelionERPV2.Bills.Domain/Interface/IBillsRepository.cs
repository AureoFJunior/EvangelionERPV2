using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.BillsModule.Domain.Interface
{
    public interface IBillsRepository<TEntity> where TEntity : class
    {
        #region Async
        Task<Bill?> GetByOrderIdAsync(Guid orderId);
        #endregion
    }
}

