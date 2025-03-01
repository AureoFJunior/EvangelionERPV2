using EvangelionERPV2.Domain.Models;

namespace EvangelionERPV2.Domain.Interfaces.Repositories
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