using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.ProductModule.Application.Services
{
    public sealed class ProductGalleryFullException()
        : InvalidOperationException($"Cada produto pode ter até {ProductService.MaxImagesPerProduct} fotos. Remova uma antes de adicionar outra.");

    /// <summary>
    /// The row gallery. Positions are always 0..n-1, and position 0 is mirrored onto the product's
    /// own picture columns, so <c>Product/Picture</c>, the list thumbnails and older screens keep
    /// showing the cover. Every change takes the product's row lock, so two uploads cannot both
    /// take the eighth slot or the same position.
    /// </summary>
    public partial class ProductService
    {
        public const int MaxImagesPerProduct = 8;

        public Task<List<ProductImage>> GetImagesAsync(Guid enterpriseId, Guid productId) =>
            _appDbContext.ProductImage.AsNoTracking()
                .Where(i => i.ProductId == productId && i.EnterpriseId == enterpriseId && i.IsActive == true)
                .OrderBy(i => i.Position).ThenBy(i => i.CreatedAt)
                .ToListAsync();

        public Task<ProductImage?> GetImageAsync(Guid enterpriseId, Guid imageId) =>
            _appDbContext.ProductImage.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == imageId && i.EnterpriseId == enterpriseId && i.IsActive == true);

        /// <summary>Appends a photo; throws <see cref="ArgumentException"/> for a refused photo and <see cref="ProductGalleryFullException"/> at the limit.</summary>
        public async Task<ProductImage> AddImageAsync(Product product, string? base64File)
        {
            var picture = ProductPictureNormalizer.FromBase64(base64File);
            if (await ActiveImages(product.Id).CountAsync() >= MaxImagesPerProduct)
                throw new ProductGalleryFullException();

            var address = await StoreImageAsync("products", product.Id, picture.Bytes);
            try
            {
                return await _productRepository.ExecuteInTransactionAsync(async () =>
                {
                    await _productRepository.LockForUpdateAsync(product.Id);
                    var currentProduct = await _appDbContext.Product.IgnoreQueryFilters().AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Id == product.Id && p.EnterpriseId == product.EnterpriseId);
                    if (currentProduct?.IsActive != true)
                        throw new InvalidOperationException("Cannot add an image to an inactive product.");

                    var images = await ActiveImages(product.Id).OrderBy(i => i.Position).ThenBy(i => i.CreatedAt).ToListAsync();
                    if (images.Count >= MaxImagesPerProduct)
                        throw new ProductGalleryFullException();

                    var image = new ProductImage
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        EnterpriseId = product.EnterpriseId!.Value,
                        Position = images.Count,
                        Adress = address,
                        Width = picture.Width,
                        Height = picture.Height,
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true
                    };
                    _appDbContext.ProductImage.Add(image);
                    images.Add(image);
                    await MirrorCoverAsync(product.Id, images);
                    await _productRepository.CommitAsync();
                    return image;
                });
            }
            catch
            {
                await DeleteImageAsync(SharedFunctions.EnsureDecryptedAddress(address));
                throw;
            }
        }

        /// <summary>Moves a photo to <paramref name="position"/> (clamped) and renumbers the rest; null when it is not this tenant's.</summary>
        public async Task<List<ProductImage>?> MoveImageAsync(Guid enterpriseId, Guid imageId, int position)
        {
            var productId = await OwnedProductIdAsync(enterpriseId, imageId);
            if (productId == null)
                return null;

            return await _productRepository.ExecuteInTransactionAsync<List<ProductImage>?>(async () =>
            {
                await _productRepository.LockForUpdateAsync(productId.Value);
                var images = await ActiveImages(productId.Value).OrderBy(i => i.Position).ThenBy(i => i.CreatedAt).ToListAsync();
                var image = images.FirstOrDefault(i => i.Id == imageId);
                if (image == null)
                    return null;

                images.Remove(image);
                images.Insert(Math.Clamp(position, 0, images.Count), image);
                await MirrorCoverAsync(productId.Value, images);
                await _productRepository.CommitAsync();
                return images;
            });
        }

        /// <summary>Removes a photo; the next one is promoted when it was the cover. False when it is not this tenant's.</summary>
        public async Task<bool> RemoveImageAsync(Guid enterpriseId, Guid imageId)
        {
            var productId = await OwnedProductIdAsync(enterpriseId, imageId);
            if (productId == null)
                return false;

            var removed = await _productRepository.ExecuteInTransactionAsync(async () =>
            {
                await _productRepository.LockForUpdateAsync(productId.Value);
                var images = await ActiveImages(productId.Value).OrderBy(i => i.Position).ThenBy(i => i.CreatedAt).ToListAsync();
                var image = images.FirstOrDefault(i => i.Id == imageId);
                if (image == null)
                    return null;

                images.Remove(image);
                _appDbContext.ProductImage.Remove(image);
                await MirrorCoverAsync(productId.Value, images);
                await _productRepository.CommitAsync();
                return image.Adress;
            });

            if (removed == null)
                return false;

            await DeleteImageAsync(SharedFunctions.EnsureDecryptedAddress(removed));
            return true;
        }

        /// <summary>Points the gallery's cover at a newly uploaded photo, creating it for a product with no gallery; caller commits.</summary>
        private async Task<ProductImage> ReplaceCoverImageAsync(Product product, string address, NormalizedPicture picture)
        {
            var cover = await ActiveImages(product.Id).OrderBy(i => i.Position).ThenBy(i => i.CreatedAt).FirstOrDefaultAsync();
            if (cover == null)
            {
                cover = new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    EnterpriseId = product.EnterpriseId!.Value,
                    Position = 0,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };
                _appDbContext.ProductImage.Add(cover);
            }

            cover.Adress = address;
            cover.Width = picture.Width;
            cover.Height = picture.Height;
            cover.UpdatedAt = DateTime.UtcNow;
            return cover;
        }

        /// <summary>
        /// Drops a cover change whose commit failed, so a later commit on the same context (the create
        /// path's stock movement) cannot save an address whose photo was just deleted.
        /// </summary>
        private async Task ForgetCoverChangeAsync(ProductImage? cover)
        {
            if (cover == null)
                return;

            var entry = _appDbContext.Entry(cover);
            if (entry.State == EntityState.Added)
                entry.State = EntityState.Detached;
            else if (entry.State != EntityState.Detached)
                await entry.ReloadAsync();
        }

        private IQueryable<ProductImage> ActiveImages(Guid productId) =>
            _appDbContext.ProductImage.Where(i => i.ProductId == productId && i.IsActive == true);

        private Task<Guid?> OwnedProductIdAsync(Guid enterpriseId, Guid imageId) =>
            _appDbContext.ProductImage.AsNoTracking()
                .Where(i => i.Id == imageId && i.EnterpriseId == enterpriseId && i.IsActive == true)
                .Select(i => (Guid?)i.ProductId)
                .FirstOrDefaultAsync();

        /// <summary>Renumbers <paramref name="ordered"/> 0..n-1 and copies position 0 onto the product; caller commits.</summary>
        private async Task MirrorCoverAsync(Guid productId, IReadOnlyList<ProductImage> ordered)
        {
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].Position = i;

            var product = await _appDbContext.Product.FirstAsync(p => p.Id == productId);
            var cover = ordered.Count > 0 ? ordered[0] : null;
            product.PictureAdress = cover?.Adress;
            product.PictureWidth = cover?.Width;
            product.PictureHeight = cover?.Height;
            product.UpdatedAt = DateTime.UtcNow;
            _productRepository.Update(product);
        }
    }
}
