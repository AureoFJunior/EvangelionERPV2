
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

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

        /// <summary>
        /// The one shared entry point for changing stock: moves the quantity, writes the
        /// <see cref="StockMovement"/> that explains it and invalidates the product cache, so no
        /// caller can do one without the others.
        /// </summary>
        Task<StockMovement> ApplyStockMovementAsync(
            Guid productId,
            Guid enterpriseId,
            decimal quantityDelta,
            EnumStockMovementType type,
            Guid? referenceId);
        public Task<TEntity> UpdateAsync(Product product);
        Task<Product> UpdatePictureAsync(ProductPicture productPicture);
        Task<string> GetProductsBodyAsync(Enterprise? enterprise);
        Task<string> GetPictureBase64Async(string? pictureAddress);
        #endregion
    }
}
