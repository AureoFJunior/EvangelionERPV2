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

namespace EvangelionERPV2.ProductModule.Application.Services
{
    public class ProductService : IProductService<Product>, IDisposable
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Product> _productRepository;
        private readonly IAmazonS3 _s3Client;
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


            var awsCredentials = this.kmsProvider.GetAWSCredentialsAsync().Result;
            _s3Client = new AmazonS3Client(awsCredentials, RegionEndpoint.USEast1);
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

                await UpdatePictureAsync(product);

                return includedProduct;

            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
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
                product.PictureAdress = SharedFunctions.Encrypt(product.PictureAdress ?? "");
                _productRepository.Update(product);
                await _productRepository.CommitAsync();

                return product;
            }
            catch (NotFoundDatabaseException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
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
                    catch (Exception ex)
                    {
                        throw;
                    }
                    finally { semaphore.Release(); }

                });
            }
            catch (NotFoundDatabaseException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
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
            catch (NotFoundDatabaseException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
            }
        }

        public async Task<Product> UpdatePictureAsync(ProductPicture productPicture)
        {
            try
            {
                Product? existentProduct = await _productRepository.GetByIdAsync(productPicture.Product.Id);
                string bucketName = _configuration.GetSection("AWSSettings")["BucketProducttName"];

                if (existentProduct == null)
                    throw new NotFoundDatabaseException($"{nameof(Product)} was not found in database.");

                string keyName = $"{productPicture.Product.Name.ClearString().Replace(" ", "-")}{DateTime.UtcNow.ToString("MM-dd-yyyy-HH:mm:ss:fff")}";
                string encryptedkeyName = SharedFunctions.Encrypt(keyName);
                productPicture.Product.UpdatedAt = DateTime.UtcNow;
                productPicture.Product.PictureAdress = encryptedkeyName;

                await DeleteOldPicture(existentProduct, bucketName);

                existentProduct = null;
                MemoryStream content = GetPictureContent(productPicture);

                await _s3Client.CreateItemAsync(bucketName, keyName, content);

                _productRepository.Update(productPicture.Product);
                await _productRepository.CommitAsync();
                return productPicture.Product;
            }
            catch (NotFoundDatabaseException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
            }
        }

        private MemoryStream GetPictureContent(ProductPicture productPicture)
        {
            var bytes = Convert.FromBase64String(productPicture.File); // Get bytes from Base64 field
            var content = new MemoryStream(bytes); // Convert the bytes into the file (Stream)
            return content;
        }

        private async Task DeleteOldPicture(Product? existentProduct, string bucketName)
        {
            if (!string.IsNullOrEmpty(existentProduct.PictureAdress))
                await _s3Client.DeleteItemAsync(bucketName, existentProduct.PictureAdress);
        }

        /// <summary>
        /// Get products stock body
        /// </summary>
        public async Task<string> GetProductsBodyAsync(Enterprise? enterprise)
        {
            if (enterprise == null)
                throw new Exception("The enterprise is null or empty");

            IEnumerable<Product> products = await _productRepository.GetAllAsync(x => x.EnterpriseId == enterprise.Id);

            if (products == null || products?.Any() == false)
            {
                Log.Logger.Warning($"Doesn't have any products");
                return null;
            }

            return await _productReportGeneratorService.GenerateStockReportAsync(enterprise);
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