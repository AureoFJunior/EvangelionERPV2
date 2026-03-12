using Amazon;
using Amazon.S3;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Collections.Concurrent;
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

        private bool disposed;

        public ProductService(EvangelionERPV2.Shared.Repositories.IRepository<Product> productRepository,
            IConfiguration configuration,
            AWSKMSKeyProvider kmsProvider,
            IProductReportGeneratorService productReportGeneratorService)
        {
            _productRepository = productRepository;
            this.kmsProvider = kmsProvider;
            _configuration = configuration;
            _productReportGeneratorService = productReportGeneratorService;
        }

        public async Task<Product> CreateAsync(ProductPicture product)
        {
            try
            {
                var existentProduct = _productRepository.GetById(product.Product.Id);
                Product includedProduct = new Product();

                if (existentProduct != null)
                    throw new InsertDatabaseException($"{nameof(Product)} already has an register in database");

                includedProduct = await _productRepository.CreateAsync(product.Product);
                await _productRepository.CommitAsync();

                if (string.IsNullOrWhiteSpace(product.File))
                    return includedProduct;

                var updatedProduct = await UpdatePictureAsync(product);
                return updatedProduct;

            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex);
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
                throw new InsertDatabaseException(ex.Message, ex);
            }
        }

        public async Task UpdateForOrder(Order order)
        {
            try
            {
                Log.Logger.Information($"Updating Products for Order [{order.Id}] at: {DateTime.UtcNow}");
                var product = new Product();
                var semaphore = new SemaphoreSlim(1, 1);
                ConcurrentBag<OrderedProduct> orderedProductsBag = new ConcurrentBag<OrderedProduct>(order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>());


                await Parallel.ForEachAsync(orderedProductsBag, async (orderedProduct, cancellationToken) =>
                {
                    try
                    {
                        if (orderedProduct == null)
                            throw new NotFoundDatabaseException($"{nameof(Product)} was not found in database.");

                        await semaphore.WaitAsync(cancellationToken);

                        product = await _productRepository.GetByIdAsync(orderedProduct.ProductId);

                        product.StorageQuantity -= orderedProduct.Quantity;

                        if (product.StorageQuantity < 0)
                            product.StorageQuantity = 0;

                        await UpdateAsync(product);
                        product = null;
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                    finally { semaphore.Release(); }

                });
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex);
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
                throw new InsertDatabaseException(ex.Message, ex);
            }
        }

        public async Task<Product> UpdatePictureAsync(ProductPicture productPicture)
        {
            try
            {
                Product? existentProduct = await _productRepository.GetByIdAsync(productPicture.Product.Id);
                string bucketName = SharedFunctions.GetProductBucketName(_configuration);

                if (existentProduct == null)
                    throw new NotFoundDatabaseException($"{nameof(Product)} was not found in database.");
                if (string.IsNullOrWhiteSpace(bucketName))
                    throw new InvalidOperationException("AWS bucket name is not configured.");

                string keyName = $"{productPicture.Product.Name.ClearString().Replace(" ", "-")}{DateTime.UtcNow.ToString("MM-dd-yyyy-HH:mm:ss:fff")}";
                string encryptedkeyName = SharedFunctions.EnsureEncryptedAddress(keyName);
                productPicture.Product.UpdatedAt = DateTime.UtcNow;
                productPicture.Product.PictureAdress = encryptedkeyName;

                var s3Client = await GetS3ClientAsync();
                await DeleteOldPicture(existentProduct, bucketName, s3Client);

                existentProduct = null;
                using var content = SharedFunctions.GetMemoryStreamFromBase64Payload(productPicture.File);
                await s3Client.CreateItemAsync(bucketName, keyName, content);

                _productRepository.Update(productPicture.Product);
                await _productRepository.CommitAsync();
                return productPicture.Product;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex);
            }
        }

        private static async Task DeleteOldPicture(Product? existentProduct, string bucketName, IAmazonS3 s3Client)
        {
            if (existentProduct == null)
                return;

            await s3Client.DeleteItemIfExistsAsync(bucketName, existentProduct.PictureAdress);
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
                Log.Logger.Warning("Product image not found in S3 for key {KeyName}", pictureAddress);
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Unable to load product image from S3 for key {KeyName}", pictureAddress);
                return string.Empty;
            }
        }

        private async Task<IAmazonS3> GetS3ClientAsync()
        {
            if (_s3Client != null)
                return _s3Client;

            var awsCredentials = await kmsProvider.GetAWSCredentialsAsync();
            _s3Client = new AmazonS3Client(awsCredentials, RegionEndpoint.USEast1);
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
