using Amazon.S3;
using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SkiaSharp;

namespace EvangelionERPV2.ProductModule.Application.Services
{
    public partial class ProductService
    {
        /// <summary>
        /// Puts photos stored before upload normalisation through <see cref="ProductPictureNormalizer"/>:
        /// every gallery or product photo without a size or with a long edge over the limit, and every
        /// family photo still carrying an EXIF rotation or over the limit. The S3 object is overwritten in
        /// place, so addresses never change, and its thumbnail is rebuilt. Idempotent; returns how many
        /// photos were rewritten.
        /// </summary>
        public async Task<int> BackfillPicturesAsync(CancellationToken cancellationToken)
        {
            var bucketName = SharedFunctions.GetProductBucketName(_configuration);
            if (string.IsNullOrWhiteSpace(bucketName))
                throw new InvalidOperationException("AWS bucket name is not configured.");

            var max = ProductPictureNormalizer.MaxLongEdge;
            var sized = (await _appDbContext.ProductImage.IgnoreQueryFilters().AsNoTracking()
                    .Where(i => i.IsActive == true && i.Adress != null && (i.Width == null || i.Height == null || i.Width > max || i.Height > max))
                    .Select(i => i.Adress!)
                    .ToListAsync(cancellationToken))
                .Concat(await _appDbContext.Product.IgnoreQueryFilters().AsNoTracking()
                    .Where(p => p.PictureAdress != null && (p.PictureWidth == null || p.PictureHeight == null || p.PictureWidth > max || p.PictureHeight > max))
                    .Select(p => p.PictureAdress!)
                    .ToListAsync(cancellationToken))
                .Distinct()
                .ToList();
            var families = (await _appDbContext.ProductVariantGroup.IgnoreQueryFilters().AsNoTracking()
                    .Where(g => g.PictureAdress != null)
                    .Select(g => g.PictureAdress!)
                    .ToListAsync(cancellationToken))
                .Except(sized)
                .ToList();

            var s3Client = await GetS3ClientAsync();
            var rewritten = 0;
            foreach (var (address, unsized) in sized.Select(a => (a, true)).Concat(families.Select(a => (a, false))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await BackfillPictureAsync(s3Client, bucketName, address, unsized, cancellationToken))
                        rewritten++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Logger.Warning("Product picture backfill skipped a photo. ErrorType={ErrorType}", ex.GetType().Name);
                }
            }

            Log.Logger.Information("Product picture backfill rewrote {Rewritten} of {Examined} photo(s).", rewritten, sized.Count + families.Count);
            return rewritten;
        }

        private async Task<bool> BackfillPictureAsync(IAmazonS3 s3Client, string bucketName, string address, bool unsized, CancellationToken cancellationToken)
        {
            var original = Convert.FromBase64String(await s3Client.GetItemBase64Async(bucketName, address));
            var (width, height, upright) = OrientedSize(original);
            if (width == 0)
                return false;

            var rewrite = unsized || !upright || Math.Max(width, height) > ProductPictureNormalizer.MaxLongEdge;
            if (rewrite)
            {
                try
                {
                    var picture = ProductPictureNormalizer.Normalize(original);
                    using (var stored = new MemoryStream(picture.Bytes))
                        await s3Client.CreateItemAsync(bucketName, SharedFunctions.EnsureDecryptedAddress(address), stored);
                    foreach (var edge in new[] { ThumbnailLongEdge, MediumLongEdge })
                    {
                        await s3Client.DeleteItemIfExistsAsync(bucketName, ThumbnailKeyFor(address, edge));
                        await BuildThumbnailAsync(s3Client, bucketName, address, edge);
                    }
                    (width, height) = (picture.Width, picture.Height);
                }
                catch (ArgumentException)
                {
                    // ponytail: a photo the upload path would refuse (short edge under 400 px, over 40 MP) keeps its bytes and only gets a size.
                    rewrite = false;
                }
            }

            await RecordSizeAsync(address, width, height, cancellationToken);
            return rewrite;
        }

        private async Task RecordSizeAsync(string address, int width, int height, CancellationToken cancellationToken)
        {
            await _appDbContext.ProductImage.IgnoreQueryFilters()
                .Where(i => i.Adress == address)
                .ExecuteUpdateAsync(set => set.SetProperty(i => i.Width, width).SetProperty(i => i.Height, height), cancellationToken);

            var products = await _appDbContext.Product.IgnoreQueryFilters()
                .Where(p => p.PictureAdress == address)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
            if (products.Count == 0)
                return;

            await _appDbContext.Product.IgnoreQueryFilters()
                .Where(p => p.PictureAdress == address)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.PictureWidth, width).SetProperty(p => p.PictureHeight, height), cancellationToken);
            foreach (var id in products)
                (_productRepository as IProductRepository<Product>)?.RemoveCachedProduct(id);
        }

        private static (int Width, int Height, bool Upright) OrientedSize(byte[] encoded)
        {
            using var codec = SKCodec.Create(new SKMemoryStream(encoded));
            if (codec == null)
                return (0, 0, false);

            var origin = codec.EncodedOrigin;
            var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            return swapsAxes
                ? (codec.Info.Height, codec.Info.Width, false)
                : (codec.Info.Width, codec.Info.Height, origin == SKEncodedOrigin.TopLeft);
        }
    }
}
