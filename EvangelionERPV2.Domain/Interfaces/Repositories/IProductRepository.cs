
using EvangelionERPV2.Domain.Models;

namespace EvangelionERPV2.Domain.Interfaces.Repositories
{
    public interface IProductRepository<TEntity> where TEntity : class
    {
        #region Sync

        #endregion

        #region Async
        Task<IEnumerable<Product>> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Product product);
        #endregion
    }
}