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

        /// <summary>
        /// The row as the database has it, never the cache. Every read that is written back must use
        /// this: other services (promotions, categories, stock) change product columns without the
        /// repository, and a cached copy written back would undo them.
        /// </summary>
        Task<Product> GetFreshByIdAsync(Guid id);

        #region Async
        Task<(IEnumerable<Product>, int)> GetAllAsyncFiltering(bool descending,
        int? pageNumber,
        int? pageSize,
        Product product);
        #endregion
    }
}