using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Moq;
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
        public async Task UpdateForOrder_WhenQuantityExceedsStorage_ClampsToZero()
        {
            var (service, productRepository, _) = CreateService();
            var productId = Guid.NewGuid();
            var order = new Order(
                DateTime.UtcNow,
                DateTime.UtcNow.AddDays(2),
                100,
                Guid.NewGuid(),
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
            var existingProduct = new Product("Product", "Desc", 10, 2, false, false, "pic", Guid.NewGuid())
            {
                Id = productId
            };
            Product? updatedProduct = null;

            productRepository.Setup(r => r.GetByIdAsync(productId)).ReturnsAsync(existingProduct);
            productRepository.Setup(r => r.Update(It.IsAny<Product>()))
                .Callback<Product>(product => updatedProduct = product)
                .Returns((Product p) => p);
            productRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            await service.UpdateForOrder(order);

            Assert.NotNull(updatedProduct);
            Assert.Equal(0, updatedProduct?.StorageQuantity);
        }

        [Fact]
        public async Task GetProductsBodyAsync_WhenEnterpriseNull_Throws()
        {
            var (service, _, _) = CreateService();

            await Assert.ThrowsAsync<Exception>(() => service.GetProductsBodyAsync(null));
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
    }
}
