
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.ProductModule.Application.Interface
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
        Task<string> GetProductsBodyAsync(Enterprise? enterprise);
        Task<string> GetPictureBase64Async(string? pictureAddress);
        #endregion
    }
}
