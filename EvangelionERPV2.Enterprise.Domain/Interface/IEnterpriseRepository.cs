using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.EnterpriseModule.Domain.Interface
{
    public interface IEnterpriseRepository<TEntity> where TEntity : class
    {
        #region Sync

        #endregion

        #region Async
        Task<IEnumerable<Enterprise>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Enterprise enterprise);
        #endregion
    }
}