using Amazon.S3;
using Amazon.Runtime;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Shared.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;

namespace EvangelionERPV2.ProductModule.Application.Services
{
    public class ProductService : IProductService<Product>, IDisposable
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Product> _productRepository;
        private IAmazonS3? _s3Client;
        private readonly IConfiguration _configuration;
        private readonly AWSKMSKeyProvider kmsProvider;
        private readonly IProductReportGeneratorService _productReportGeneratorService;
        private readonly AppDbContext? _appDbContext;

        private bool disposed;

        public ProductService(EvangelionERPV2.Shared.Repositories.IRepository<Product> productRepository,
            IConfiguration configuration,
            AWSKMSKeyProvider kmsProvider,
            IProductReportGeneratorService productReportGeneratorService,
            AppDbContext? appDbContext = null)
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

        public async Task UpdateForOrder(Order order)
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

                var now = DateTime.UtcNow;

                foreach (var orderedProduct in orderedProducts)
                {
                    if (_appDbContext != null)
                    {
                        var affectedRows = await _appDbContext.Database.ExecuteSqlInterpolatedAsync(
                            $"UPDATE Product SET StorageQuantity = StorageQuantity - {orderedProduct.Quantity}, UpdatedAt = {now} WHERE Id = {orderedProduct.ProductId} AND EnterpriseId = {enterpriseId} AND IsActive = 1 AND StorageQuantity >= {orderedProduct.Quantity}");

                        if (affectedRows <= 0)
                            throw new InsertDatabaseException($"Insufficient stock for product [{orderedProduct.ProductId}].");

                        continue;
                    }

                    var product = await _productRepository.GetByIdAsync(orderedProduct.ProductId);
                    if (product == null || product.EnterpriseId != enterpriseId || product.IsActive != true)
                        throw new NotFoundDatabaseException($"{nameof(Product)} was not found in database.");

                    if (product.StorageQuantity < orderedProduct.Quantity)
                        throw new InsertDatabaseException($"Insufficient stock for product [{orderedProduct.ProductId}].");

                    product.StorageQuantity -= orderedProduct.Quantity;

                    product.UpdatedAt = now;
                    _productRepository.Update(product);
                    await _productRepository.CommitAsync();
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

        private async Task<IAmazonS3> GetS3ClientAsync()
        {
            if (_s3Client != null)
                return _s3Client;

            var awsCredentials = await kmsProvider.GetAWSCredentialsAsync();
            var awsRegion = SharedFunctions.GetAWSRegion(_configuration);
            _s3Client = new AmazonS3Client(awsCredentials, awsRegion);
            return _s3Client;
        }

        #region Dispose Pattern
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources here.
                    (_s3Client as IDisposable)?.Dispose();
                    (_productRepository as IDisposable)?.Dispose();
                    (kmsProvider as IDisposable)?.Dispose();
                }

                // Dispose unmanaged resources here.
                // For example:
                // Close file handles, release COM objects.

                disposed = true;
            }
        }

        // Destructor for finalization code
        ~ProductService()
        {
            Dispose(false);
        }

        #endregion
    }
}
