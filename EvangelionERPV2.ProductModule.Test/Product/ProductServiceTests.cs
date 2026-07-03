using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.S3;
using Amazon.S3.Model;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Reflection;
using Xunit;

namespace EvangelionERPV2.ProductModule.Test
{
    public class ProductServiceTests
    {
        [Fact]
        public async Task UpdateAsync_WhenProductMissing_ThrowsNotFoundDatabaseException()
        {
            var (service, productRepository, _) = CreateService();
            var product = new Product("Product", "Desc", 10, 5, false, false, "pic", Guid.NewGuid())
            {
                Id = Guid.NewGuid()
            };

            productRepository.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync((Product)null!);

            await Assert.ThrowsAsync<NotFoundDatabaseException>(() => service.UpdateAsync(product));
        }

        [Fact]
        public async Task UpdateAsync_WhenValid_UpdatesAndCommits()
        {
            var (service, productRepository, _) = CreateService();
            var product = new Product("Product", "Desc", 10, 5, false, false, "pic", Guid.NewGuid())
            {
                Id = Guid.NewGuid()
            };

            productRepository.Setup(r => r.GetByIdAsync(product.Id)).ReturnsAsync(product);
            productRepository.Setup(r => r.Update(It.IsAny<Product>())).Returns((Product p) => p);
            productRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var result = await service.UpdateAsync(product);
            var updatedAt = result.UpdatedAt ?? DateTime.MinValue;

            Assert.Same(product, result);
            Assert.NotEqual(DateTime.MinValue, updatedAt);
            productRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void Delete_WhenProductMissing_ThrowsNotFoundDatabaseException()
        {
            var (service, productRepository, _) = CreateService();
            var productId = Guid.NewGuid();

            productRepository.Setup(r => r.GetById(productId)).Returns((Product)null!);

            Assert.Throws<NotFoundDatabaseException>(() => service.Delete(productId));
        }

        [Fact]
        public void Delete_WhenValid_SetsInactive()
        {
            var (service, productRepository, _) = CreateService();
            var product = new Product("Product", "Desc", 10, 5, false, false, "pic", Guid.NewGuid())
            {
                Id = Guid.NewGuid(),
                IsActive = true
            };

            productRepository.Setup(r => r.GetById(product.Id)).Returns(product);
            productRepository.Setup(r => r.Update(It.IsAny<Product>())).Returns((Product p) => p);

            var result = service.Delete(product.Id);
            var updatedAt = result.UpdatedAt ?? DateTime.MinValue;

            Assert.Same(product, result);
            Assert.False(result.IsActive ?? true);
            Assert.NotEqual(DateTime.MinValue, updatedAt);
            productRepository.Verify(r => r.Commit(), Times.Once);
        }

        [Fact]
        public async Task UpdateForOrder_WhenQuantityExceedsStorage_ThrowsAndLeavesStockUnchanged()
        {
            var (service, productRepository, _) = CreateService();
            var productId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var order = new Order(
                DateTime.UtcNow,
                DateTime.UtcNow.AddDays(2),
                100,
                enterpriseId,
                Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        ProductId = productId,
                        Quantity = 5,
                        Value = 10,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        IsActive = true
                    }
                },
                Guid.NewGuid());
            var existingProduct = new Product("Product", "Desc", 10, 2, false, false, "pic", enterpriseId)
            {
                Id = productId,
                IsActive = true
            };
            Product? updatedProduct = null;

            productRepository.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(existingProduct);
            productRepository.Setup(r => r.Update(It.IsAny<Product>()))
                .Callback<Product>(product => updatedProduct = product)
                .Returns((Product p) => p);
            productRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            await Assert.ThrowsAsync<InsertDatabaseException>(() => service.UpdateForOrder(order));

            Assert.Null(updatedProduct);
            Assert.Equal(2, existingProduct.StorageQuantity);
            productRepository.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
            productRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetProductsBodyAsync_WhenEnterpriseNull_Throws()
        {
            var (service, _, _) = CreateService();

            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => service.GetProductsBodyAsync(null));
            Assert.Equal("enterprise", exception.ParamName);
        }

        [Fact]
        public async Task GetProductsBodyAsync_WhenNoProducts_ReturnsEmptyString()
        {
            var (service, productRepository, reportGenerator) = CreateService();
            var enterprise = new Enterprise("Enterprise", "11999999999", "enterprise@test.com", "Street", true)
            {
                Id = Guid.NewGuid()
            };

            productRepository.Setup(r => r.GetAllAsync(It.IsAny<Func<Product, bool>>()))
                .ReturnsAsync(new List<Product>());

            var result = await service.GetProductsBodyAsync(enterprise);

            Assert.Equal(string.Empty, result);
            reportGenerator.Verify(r => r.GenerateStockReportAsync(It.IsAny<Enterprise>()), Times.Never);
        }

        [Fact]
        public async Task GetProductsBodyAsync_WhenProductsExist_ReturnsReport()
        {
            var (service, productRepository, reportGenerator) = CreateService();
            var enterprise = new Enterprise("Enterprise", "11999999999", "enterprise@test.com", "Street", true)
            {
                Id = Guid.NewGuid()
            };
            var products = new List<Product>
            {
                new Product("Product", "Desc", 10, 5, false, false, "pic", enterprise.Id)
                {
                    Id = Guid.NewGuid()
                }
            };

            productRepository.Setup(r => r.GetAllAsync(It.IsAny<Func<Product, bool>>()))
                .ReturnsAsync(products);
            reportGenerator.Setup(r => r.GenerateStockReportAsync(enterprise)).ReturnsAsync("report");

            var result = await service.GetProductsBodyAsync(enterprise);

            Assert.Equal("report", result);
            reportGenerator.Verify(r => r.GenerateStockReportAsync(enterprise), Times.Once);
        }

        [Fact]
        public async Task UpdatePictureAsync_WhenOldPictureDeletionFails_DoesNotFailSuccessfulUpdate()
        {
            EnsureEncryptionKeyInitialized();

            var (service, productRepository, _, s3ClientMock) = CreateServiceWithS3Mock();
            var productId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var existentProduct = new Product("Product", "Desc", 10, 5, false, false, "products/old-picture-key", enterpriseId)
            {
                Id = productId
            };

            var payload = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D });
            var request = new ProductPicture
            {
                Product = new Product("Product", "Desc", 10, 5, false, false, existentProduct.PictureAdress, enterpriseId)
                {
                    Id = productId
                },
                File = payload
            };

            productRepository.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(existentProduct);
            productRepository.Setup(r => r.Update(It.IsAny<Product>())).Returns((Product product) => product);
            productRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            s3ClientMock
                .Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PutObjectResponse());

            s3ClientMock
                .Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
                .Returns<DeleteObjectRequest, CancellationToken>((request, _) =>
                {
                    if (request.Key == "products/old-picture-key")
                        throw new InvalidOperationException("s3 delete old failed");

                    return Task.FromResult(new DeleteObjectResponse());
                });

            var result = await service.UpdatePictureAsync(request);

            Assert.NotNull(result);
            productRepository.Verify(r => r.Update(It.IsAny<Product>()), Times.Once);
            productRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
            s3ClientMock.Verify(
                s => s.DeleteObjectAsync(
                    It.Is<DeleteObjectRequest>(request => request.Key == "products/old-picture-key"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task CreateAsync_WhenFileIsMissing_CreatesProductWithoutUploadingImage()
        {
            var (service, productRepository, _, s3ClientMock) = CreateServiceWithS3Mock();
            var enterpriseId = Guid.NewGuid();
            Product? createdEntity = null;

            productRepository
                .Setup(r => r.CreateAsync(It.IsAny<Product>()))
                .ReturnsAsync((Product product) =>
                {
                    createdEntity = product;
                    return product;
                });

            productRepository
                .Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var request = new ProductPicture
            {
                Product = new Product("Product", "Desc", 10, 5, false, false, string.Empty, enterpriseId),
                File = string.Empty
            };

            var result = await service.CreateAsync(request);

            Assert.NotNull(result);
            Assert.Same(createdEntity, result);
            productRepository.Verify(r => r.CreateAsync(It.IsAny<Product>()), Times.Once);
            productRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
            s3ClientMock.Verify(
                s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CreateAsync_WhenImageUploadFails_ThrowsInsertDatabaseException()
        {
            EnsureEncryptionKeyInitialized();

            var (service, productRepository, _, s3ClientMock) = CreateServiceWithS3Mock();
            var enterpriseId = Guid.NewGuid();
            Product? createdEntity = null;

            productRepository
                .Setup(r => r.CreateAsync(It.IsAny<Product>()))
                .ReturnsAsync((Product product) =>
                {
                    createdEntity = product;
                    return product;
                });

            productRepository
                .Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            productRepository
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync(() => createdEntity!);

            var request = new ProductPicture
            {
                Product = new Product("Product", "Desc", 10, 5, false, false, string.Empty, enterpriseId),
                File = "not-a-valid-base64"
            };

            await Assert.ThrowsAsync<InsertDatabaseException>(() => service.CreateAsync(request));
            productRepository.Verify(r => r.CreateAsync(It.IsAny<Product>()), Times.Once);
            productRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
            s3ClientMock.Verify(
                s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static (ProductService service,
            Mock<IRepository<Product>> productRepository,
            Mock<IProductReportGeneratorService> reportGenerator) CreateService()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AWSSettings:SecretName"] = "test-secret"
                })
                .Build();
            var secretsManager = new Mock<IAmazonSecretsManager>();
            secretsManager.Setup(s => s.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse
                {
                    SecretString = "{\"access-key-id\":\"test\",\"secret-access-key\":\"test\"}"
                });
            var kmsProvider = new AWSKMSKeyProvider(secretsManager.Object, configuration);
            var productRepository = new Mock<IRepository<Product>>();
            var reportGenerator = new Mock<IProductReportGeneratorService>();

            var service = new ProductService(productRepository.Object, configuration, kmsProvider, reportGenerator.Object);

            return (service, productRepository, reportGenerator);
        }

        private static (ProductService service,
            Mock<IRepository<Product>> productRepository,
            Mock<IProductReportGeneratorService> reportGenerator,
            Mock<IAmazonS3> s3ClientMock) CreateServiceWithS3Mock()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AWSSettings:SecretName"] = "test-secret",
                    ["AWSSettings:BucketProducttName"] = "test-product-bucket"
                })
                .Build();

            var secretsManager = new Mock<IAmazonSecretsManager>();
            secretsManager.Setup(s => s.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse
                {
                    SecretString = "{\"access-key-id\":\"test\",\"secret-access-key\":\"test\"}"
                });

            var kmsProvider = new AWSKMSKeyProvider(secretsManager.Object, configuration);
            var productRepository = new Mock<IRepository<Product>>();
            var reportGenerator = new Mock<IProductReportGeneratorService>();
            var s3ClientMock = new Mock<IAmazonS3>();

            var service = new ProductService(productRepository.Object, configuration, kmsProvider, reportGenerator.Object);

            typeof(ProductService)
                .GetField("_s3Client", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(service, s3ClientMock.Object);

            return (service, productRepository, reportGenerator, s3ClientMock);
        }

        private static void EnsureEncryptionKeyInitialized()
        {
            var encryptionField = typeof(SharedFunctions)
                .GetField("_encryptionKey", BindingFlags.NonPublic | BindingFlags.Static);

            if (encryptionField == null)
                return;

            var currentValue = encryptionField.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(currentValue))
                return;

            var keyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
            encryptionField.SetValue(null, Convert.ToBase64String(keyBytes));
        }
    }
}
