using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.ProductModule.Application.Interface
{
    public interface IOrderedProductService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);
        public TEntity Update(OrderedProduct orderedProduct);
        #endregion

        #region Async
        public Task<TEntity> CreateAsync(OrderedProduct orderedProduct);
        #endregion
    }
}