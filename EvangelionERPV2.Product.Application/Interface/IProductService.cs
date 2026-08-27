
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
        /// <param name="allowNegativeStock">
        /// Threaded straight to <see cref="ApplyStockMovementAsync"/>. See the note there: only a
        /// PDV offline sale sets it, because that sale is already history by the time it arrives.
        /// </param>
        public Task UpdateForOrder(Order order, bool allowNegativeStock = false);

        /// <summary>
        /// The one shared entry point for changing stock: moves the quantity, writes the
        /// <see cref="StockMovement"/> that explains it and invalidates the product cache, so no
        /// caller can do one without the others.
        /// </summary>
        /// <param name="allowNegative">
        /// Lets the balance go below zero. Only the PDV offline-sale sync passes true: that sale
        /// already happened, the customer already left with the goods, and refusing the movement
        /// would lose the sale rather than restore the stock. The resulting negative balance is a
        /// count discrepancy to be reported, which is the honest record of what occurred.
        /// </param>
        Task<StockMovement> ApplyStockMovementAsync(
            Guid productId,
            Guid enterpriseId,
            decimal quantityDelta,
            EnumStockMovementType type,
            Guid? referenceId,
            bool allowNegative = false);
        public Task<TEntity> UpdateAsync(Product product);
        Task<Product> UpdatePictureAsync(ProductPicture productPicture);
        Task<string> GetProductsBodyAsync(Enterprise? enterprise);
        Task<string> GetPictureBase64Async(string? pictureAddress);

        /// <summary>
        /// A 200 px thumbnail of the product picture, base64-encoded, with a hash of its bytes.
        /// </summary>
        /// <remarks>
        /// Generated on first request and cached in the bucket, so this costs one resize per image
        /// for its lifetime rather than one per till per sync. Empty string and null hash when the
        /// product has no picture or the object is unreadable.
        /// </remarks>
        Task<(string Base64, string? Hash)> GetThumbnailBase64Async(string? pictureAddress);
        #endregion
    }
}
