using EvangelionERPV2.Domain.Models;

namespace EvangelionERPV2.Domain.Interfaces.Services
{
    public interface IProductService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);
        #endregion

        #region Async
        public Task<TEntity> CreateAsync(ProductPicture product);
        public Task UpdateForOrder(Order order);
        public Task<TEntity> UpdateAsync(Product product);
        Task<Product> UpdatePictureAsync(ProductPicture productPicture);
        #endregion
    }
}