using EvangelionERPV2.Domain.Models;

namespace EvangelionERPV2.Domain.Interfaces.Services
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