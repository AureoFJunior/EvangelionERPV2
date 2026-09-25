using Amazon.S3;
using Amazon.Runtime;
using SkiaSharp;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Shared.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;

namespace EvangelionERPV2.ProductModule.Application.Services
{
    public class ProductService : IProductService<Product>
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Product> _productRepository;
        private IAmazonS3? _s3Client;
        private readonly IConfiguration _configuration;
        private readonly AWSKMSKeyProvider kmsProvider;
        private readonly IProductReportGeneratorService _productReportGeneratorService;
        private readonly AppDbContext _appDbContext;


        public ProductService(EvangelionERPV2.Shared.Repositories.IRepository<Product> productRepository,
            IConfiguration configuration,
            AWSKMSKeyProvider kmsProvider,
            IProductReportGeneratorService productReportGeneratorService,
            AppDbContext appDbContext)
        {
            _productRepository = productRepository;
            this.kmsProvider = kmsProvider;
            _configuration = configuration;
            _productReportGeneratorService = productReportGeneratorService;
            _appDbContext = appDbContext;
        }

        public async Task<Product> CreateAsync(ProductPicture product)
        {
            try
            {
                if (product?.Product == null)
                    throw new InsertDatabaseException($"{nameof(Product)} is null");

                product.Product.Id = Guid.NewGuid();

                Product includedProduct = new Product();
                includedProduct = await _productRepository.CreateAsync(product.Product);
                await _productRepository.CommitAsync();

                if (string.IsNullOrWhiteSpace(product.File))
                    return includedProduct;

                try
                {
                    var updatedProduct = await UpdatePictureAsync(product);
                    return updatedProduct;
                }
                catch (Exception ex)
                {
                    var rootError = ex.GetBaseException();
                    Log.Logger.Warning(
                        ex,
                        "Product image upload failed after product creation. ProductId={ProductId} ErrorType={ErrorType} RootErrorType={RootErrorType} RootErrorMessage={RootErrorMessage}",
                        includedProduct.Id,
                        ex.GetType().Name,
                        rootError.GetType().Name,
                        rootError.Message);

                    throw new InsertDatabaseException("Product image upload failed during creation.", ex);
                }
            }
            catch (InsertDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while creating product.", ex);
            }
        }

        public async Task<Product> UpdateAsync(Product product)
        {
            try
            {
                Product existentProduct = await _productRepository.GetByIdAsync(product.Id);

                if (existentProduct == null)
                    throw new NotFoundDatabaseException($"{nameof(Product)} was not found in database.");

                _productRepository.DetachEntity(existentProduct);
                product.UpdatedAt = DateTime.UtcNow;
                product.PictureAdress = SharedFunctions.EnsureEncryptedAddress(product.PictureAdress);
                _productRepository.Update(product);
                await _productRepository.CommitAsync();

                return product;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while updating product.", ex);
            }
        }

        public async Task UpdateForOrder(Order order, bool allowNegativeStock = false)
        {
            try
            {
                Log.Logger.Information($"Updating Products for Order [{order.Id}] at: {DateTime.UtcNow}");

                var orderedProducts = (order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>())
                    .Where(orderedProduct => orderedProduct != null)
                    .ToList();

                if (!orderedProducts.Any())
                    return;

                var enterpriseId = order.EnterpriseId ?? Guid.Empty;
                if (enterpriseId == Guid.Empty)
                    throw new InsertDatabaseException("Order enterprise is required.");

                foreach (var orderedProduct in orderedProducts)
                {
                    await ApplyStockMovementAsync(
                        orderedProduct.ProductId,
                        enterpriseId,
                        -orderedProduct.Quantity,
                        EnumStockMovementType.Sale,
                        order.Id,
                        allowNegativeStock);
                }
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while updating product stock for order.", ex);
            }
        }

        /// <summary>
        /// The only place <see cref="Product.StorageQuantity"/> is allowed to move.
        /// </summary>
        /// <param name="quantityDelta">Signed: negative consumes stock, positive returns it.</param>
        public async Task<StockMovement> ApplyStockMovementAsync(
            Guid productId,
            Guid enterpriseId,
            decimal quantityDelta,
            EnumStockMovementType type,
            Guid? referenceId,
            bool allowNegative = false)
        {
            if (productId == Guid.Empty)
                throw new InsertDatabaseException("Product is required for a stock movement.");

            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Enterprise is required for a stock movement.");

            PeriodLock.Assert(await GetClosedThroughUtcAsync(enterpriseId), DateTime.UtcNow, "This stock movement");

            if (_appDbContext.Database.CurrentTransaction != null)
                return await ApplyStockMovementCoreAsync(productId, enterpriseId, quantityDelta, type, referenceId, allowNegative);

            return await _productRepository.ExecuteInTransactionAsync(
                () => ApplyStockMovementCoreAsync(productId, enterpriseId, quantityDelta, type, referenceId, allowNegative));
        }

        /// <summary>
        /// The tenant's period lock, read without tracking. Null when nothing is closed.
        /// </summary>
        /// <remarks>
        /// ponytail: one single-column read per movement, so an order with ten lines pays for ten.
        /// It is a primary-key lookup on a tiny table; cache it per request if a profile ever
        /// disagrees.
        /// </remarks>
        private async Task<DateTime?> GetClosedThroughUtcAsync(Guid enterpriseId)
        {
            return await _appDbContext.Enterprise
                .AsNoTracking()
                .Where(x => x.Id == enterpriseId)
                .Select(x => x.ClosedThroughUtc)
                .FirstOrDefaultAsync();
        }

        private async Task<StockMovement> ApplyStockMovementCoreAsync(
            Guid productId,
            Guid enterpriseId,
            decimal quantityDelta,
            EnumStockMovementType type,
            Guid? referenceId,
            bool allowNegative)
        {
            var now = DateTime.UtcNow;

            var affectedRows = allowNegative
                ? await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Product SET StorageQuantity = StorageQuantity + {quantityDelta}, UpdatedAt = {now} WHERE Id = {productId} AND EnterpriseId = {enterpriseId} AND IsActive = 1")
                : await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Product SET StorageQuantity = StorageQuantity + {quantityDelta}, UpdatedAt = {now} WHERE Id = {productId} AND EnterpriseId = {enterpriseId} AND IsActive = 1 AND StorageQuantity + {quantityDelta} >= 0");

            if (affectedRows <= 0)
                throw new InsertDatabaseException(quantityDelta < 0 && !allowNegative
                    ? $"Insufficient stock for product [{productId}]."
                    : $"Product [{productId}] was not found.");

            var balanceAfter = await _appDbContext.Product
                .AsNoTracking()
                .Where(product => product.Id == productId)
                .Select(product => product.StorageQuantity)
                .FirstAsync();

            var movement = new StockMovement
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                EnterpriseId = enterpriseId,
                Quantity = quantityDelta,
                BalanceAfter = balanceAfter,
                Type = (int)type,
                ReferenceId = referenceId,
                CreatedAt = now,
                IsActive = true
            };

            _appDbContext.StockMovement.Add(movement);
            await _appDbContext.SaveChangesAsync();

            (_productRepository as IProductRepository<Product>)?.RemoveCachedProduct(productId);

            return movement;
        }

        public Product Delete(Guid id)
        {
            try
            {
                Product product = _productRepository.GetById(id);
                Product deletedProduct = new Product();

                if (product == null)
                    throw new NotFoundDatabaseException($"{nameof(Product)} was not found in database.");

                product.IsActive = false;
                product.UpdatedAt = DateTime.UtcNow;
                deletedProduct = _productRepository.Update(product);
                _productRepository.Commit();
                return deletedProduct;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while updating product picture.", ex);
            }
        }

        public async Task<Product> UpdatePictureAsync(ProductPicture productPicture)
        {
            var productId = productPicture?.Product?.Id ?? Guid.Empty;
            var payloadLength = productPicture?.File?.Length ?? 0;
            var currentStage = "start";
            var bucketName = string.Empty;
            var keyName = string.Empty;
            var encryptedKeyName = string.Empty;

            try
            {
                currentStage = "validate-payload";
                if (productPicture?.Product == null)
                    throw new InsertDatabaseException("Product image payload is invalid.");

                if (string.IsNullOrWhiteSpace(productPicture.File))
                    throw new InsertDatabaseException("Product image file is required.");

                currentStage = "resolve-product";
                Product? existentProduct = await _productRepository.GetByIdAsync(productPicture.Product.Id);

                if (existentProduct == null)
                    throw new NotFoundDatabaseException($"{nameof(Product)} was not found in database.");

                currentStage = "resolve-bucket";
                bucketName = SharedFunctions.GetProductBucketName(_configuration);
                if (string.IsNullOrWhiteSpace(bucketName))
                    throw new InvalidOperationException("AWS bucket name is not configured.");

                currentStage = "prepare-storage-key";
                keyName = SharedFunctions.GenerateStorageObjectKey("products", productPicture.Product.Id);
                encryptedKeyName = SharedFunctions.EnsureEncryptedAddress(keyName);
                productPicture.Product.UpdatedAt = DateTime.UtcNow;
                productPicture.Product.PictureAdress = encryptedKeyName;
                var oldPictureAddress = existentProduct.PictureAdress;

                currentStage = "get-s3-client";
                var s3Client = await GetS3ClientAsync();

                currentStage = "decode-base64-payload";
                using var content = SharedFunctions.GetMemoryStreamFromBase64Payload(productPicture.File);

                currentStage = "upload-to-s3";
                await s3Client.CreateItemAsync(bucketName, keyName, content);

                try
                {
                    currentStage = "persist-db-address";
                    _productRepository.Update(productPicture.Product);
                    await _productRepository.CommitAsync();
                }
                catch
                {
                    currentStage = "rollback-s3-after-db-failure";
                    await s3Client.DeleteItemIfExistsAsync(bucketName, keyName);
                    throw;
                }

                currentStage = "cleanup-old-picture";
                await TryDeleteOldPictureAsync(s3Client, bucketName, oldPictureAddress);

                Log.Logger.Information(
                    "Product image upload succeeded. ProductId={ProductId} Bucket={Bucket} Key={Key} PayloadLength={PayloadLength}",
                    productPicture.Product.Id,
                    bucketName,
                    keyName,
                    payloadLength);
                return productPicture.Product;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (InsertDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var rootError = ex.GetBaseException();
                var awsRegion = SharedFunctions.GetAWSRegion(_configuration).SystemName;

                if (rootError is AmazonServiceException amazonServiceException)
                {
                    Log.Logger.Warning(
                        ex,
                        "Product image upload failed. ProductId={ProductId} Stage={Stage} Bucket={Bucket} Key={Key} EncryptedKeyEmpty={EncryptedKeyEmpty} PayloadLength={PayloadLength} Region={Region} AwsErrorCode={AwsErrorCode} AwsStatusCode={AwsStatusCode} AwsRequestId={AwsRequestId} RootErrorType={RootErrorType} RootErrorMessage={RootErrorMessage}",
                        productId,
                        currentStage,
                        bucketName,
                        keyName,
                        string.IsNullOrWhiteSpace(encryptedKeyName),
                        payloadLength,
                        awsRegion,
                        amazonServiceException.ErrorCode,
                        (int)amazonServiceException.StatusCode,
                        amazonServiceException.RequestId,
                        rootError.GetType().Name,
                        rootError.Message);
                }
                else
                {
                    Log.Logger.Warning(
                        ex,
                        "Product image upload failed. ProductId={ProductId} Stage={Stage} Bucket={Bucket} Key={Key} EncryptedKeyEmpty={EncryptedKeyEmpty} PayloadLength={PayloadLength} Region={Region} RootErrorType={RootErrorType} RootErrorMessage={RootErrorMessage}",
                        productId,
                        currentStage,
                        bucketName,
                        keyName,
                        string.IsNullOrWhiteSpace(encryptedKeyName),
                        payloadLength,
                        awsRegion,
                        rootError.GetType().Name,
                        rootError.Message);
                }

                throw new InsertDatabaseException("Unexpected error while updating product picture.", ex);
            }
        }

        private static async Task TryDeleteOldPictureAsync(IAmazonS3 s3Client, string bucketName, string? oldPictureAddress)
        {
            try
            {
                await s3Client.DeleteItemIfExistsAsync(bucketName, oldPictureAddress);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(
                    "Unable to delete old product image from S3 after successful picture update. ErrorType={ErrorType}",
                    ex.GetType().Name);
            }
        }

        /// <summary>
        /// Get products stock body
        /// </summary>
        public async Task<string> GetProductsBodyAsync(Enterprise? enterprise)
        {
            if (enterprise == null)
                throw new ArgumentNullException(nameof(enterprise), "The enterprise is null or empty");

            IEnumerable<Product> products = await _productRepository.GetAllAsync(x => x.EnterpriseId == enterprise.Id);

            if (products == null || products?.Any() == false)
            {
                Log.Logger.Warning($"Doesn't have any products");
                return string.Empty;
            }

            return await _productReportGeneratorService.GenerateStockReportAsync(enterprise);
        }

        public async Task<string> GetPictureBase64Async(string? pictureAddress)
        {
            try
            {
                string bucketName = SharedFunctions.GetProductBucketName(_configuration);
                if (string.IsNullOrWhiteSpace(bucketName))
                    throw new InvalidOperationException("AWS bucket name is not configured.");

                var s3Client = await GetS3ClientAsync();
                return await s3Client.GetItemBase64Async(bucketName, pictureAddress);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.BadRequest)
            {
                Log.Logger.Warning("Product image not found in S3.");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(
                    "Unable to load product image from S3. ErrorType={ErrorType}",
                    ex.GetType().Name);
                return string.Empty;
            }
        }

        /// <summary>The long edge of a stored thumbnail, in pixels.</summary>
        public const int ThumbnailLongEdge = 200;

        /// <summary>
        /// Shrinks an encoded image so its long edge is at most <paramref name="longEdge"/>.
        /// </summary>
        public static byte[] ResizeToThumbnail(byte[] original, int longEdge)
        {
            try
            {
                using var source = SKBitmap.Decode(original);
                if (source == null)
                    return [];

                var scale = (double)longEdge / Math.Max(source.Width, source.Height);
                if (scale >= 1.0)
                    return original;

                var width = Math.Max(1, (int)Math.Round(source.Width * scale));
                var height = Math.Max(1, (int)Math.Round(source.Height * scale));

                using var resized = source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                if (resized == null)
                    return [];

                using var image = SKImage.FromBitmap(resized);
                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);

                return data.ToArray();
            }
            catch (Exception)
            {
                return [];
            }
        }

        public async Task<(string Base64, string? Hash)> GetThumbnailBase64Async(string? pictureAddress)
        {
            if (string.IsNullOrWhiteSpace(pictureAddress))
                return (string.Empty, null);

            try
            {
                var bucketName = SharedFunctions.GetProductBucketName(_configuration);
                if (string.IsNullOrWhiteSpace(bucketName))
                    throw new InvalidOperationException("AWS bucket name is not configured.");

                var s3Client = await GetS3ClientAsync();
                return await BuildThumbnailAsync(s3Client, bucketName, pictureAddress);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("Unable to build product thumbnail. ErrorType={ErrorType}", ex.GetType().Name);
                return (string.Empty, null);
            }
        }

        /// <summary>
        /// The read-through cache: serves a cached thumbnail if one exists, otherwise fetches the
        /// original, resizes it and writes the result back.
        /// </summary>
        public static async Task<(string Base64, string? Hash)> BuildThumbnailAsync(IAmazonS3 s3Client, string bucketName, string pictureAddress)
        {
            var thumbnailKey = ThumbnailKeyFor(pictureAddress);

            var cached = string.Empty;
            try
            {
                cached = await s3Client.GetItemBase64Async(bucketName, thumbnailKey);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.BadRequest)
            {
            }

            if (!string.IsNullOrWhiteSpace(cached))
                return (cached, HashOf(cached));

            string originalBase64;
            try
            {
                originalBase64 = await s3Client.GetItemBase64Async(bucketName, pictureAddress);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.BadRequest)
            {
                Log.Logger.Warning("Product image not found in S3.");
                return (string.Empty, null);
            }

            if (string.IsNullOrWhiteSpace(originalBase64))
                return (string.Empty, null);

            var thumbnail = ResizeToThumbnail(Convert.FromBase64String(originalBase64), ThumbnailLongEdge);
            if (thumbnail.Length == 0)
                return (string.Empty, null);

            try
            {
                using var upload = new MemoryStream(thumbnail);
                await s3Client.CreateItemAsync(bucketName, thumbnailKey, upload);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("Could not cache product thumbnail. ErrorType={ErrorType}", ex.GetType().Name);
            }

            var encoded = Convert.ToBase64String(thumbnail);
            return (encoded, HashOf(encoded));
        }

        /// <summary>Where the thumbnail for an original lives. Same bucket, <c>thumb/</c> prefix.</summary>
        private static string ThumbnailKeyFor(string pictureAddress) => $"thumb/{pictureAddress}";

        /// <summary>
        /// Identifies the bytes a till already holds, so an unchanged image is never sent twice.
        /// </summary>
        private static string HashOf(string base64)
        {
            var digest = System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(base64));

            return "sha256:" + Convert.ToHexStringLower(digest);
        }

        private async Task<IAmazonS3> GetS3ClientAsync()
        {
            if (_s3Client != null)
                return _s3Client;

            var awsCredentials = await kmsProvider.GetAWSCredentialsAsync();
            var awsRegion = SharedFunctions.GetAWSRegion(_configuration);
            _s3Client = new AmazonS3Client(awsCredentials, awsRegion);
            return _s3Client;
        }

    }
}
