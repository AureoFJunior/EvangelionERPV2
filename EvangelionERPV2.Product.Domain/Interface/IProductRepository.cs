using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.ProductModule.Domain.Interface
{
    public interface IProductRepository<TEntity> where TEntity : class
    {
        #region Sync

        #endregion

        #region Async
        Task<(IEnumerable<Product>, int)> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Product product);
        #endregion
    }
}