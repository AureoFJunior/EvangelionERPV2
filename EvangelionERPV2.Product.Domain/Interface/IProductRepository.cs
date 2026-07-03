using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.ProductModule.Domain.Interface
{
    public interface IProductRepository<TEntity> where TEntity : class
    {
        #region Sync
        /// <summary>
        /// Removes the cached entry for a product. Must be called whenever the product row is
        /// changed outside the repository's Update/UpdateRange path (e.g. guarded raw SQL stock
        /// decrements), otherwise cached reads keep serving the stale value for the cache TTL.
        /// </summary>
        void RemoveCachedProduct(Guid id);
        #endregion

        #region Async
        Task<(IEnumerable<Product>, int)> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Product product);
        #endregion
    }
}