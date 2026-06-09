using EvangelionERPV2.ProductModule.Domain.Repositories;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using System.Text;
using System.Text.Json;

namespace EvangelionERPV2.ProductModule.Test
{
    public class ProductRepositoryTests
    {
        [Fact]
        public async Task GetByIdAsync_WithPlainCachedPictureAddress_PreservesPictureKey()
        {
            var productId = Guid.NewGuid();
            var cachedProduct = new Product
            {
                Id = productId,
                Name = "Cached product",
                PictureAdress = "products/sample-image",
                EnterpriseId = Guid.NewGuid(),
                IsActive = true
            };
            var cache = new Mock<IDistributedCache>(MockBehavior.Strict);
            cache
                .Setup(c => c.GetAsync($"Product:{productId}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cachedProduct)));

            await using var context = CreateContext();
            var repository = new ProductRepository(context, cache.Object);

            var result = await repository.GetByIdAsync(productId);

            Assert.Equal("products/sample-image", result.PictureAdress);
            cache.Verify(c => c.GetAsync($"Product:{productId}", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void Update_RemovesProductCacheEntry()
        {
            var productId = Guid.NewGuid();
            var cache = new Mock<IDistributedCache>(MockBehavior.Strict);
            cache.Setup(c => c.Remove($"Product:{productId}"));

            using var context = CreateContext();
            var repository = new ProductRepository(context, cache.Object);

            repository.Update(new Product
            {
                Id = productId,
                Name = "Updated product",
                PictureAdress = "products/new-image",
                EnterpriseId = Guid.NewGuid(),
                IsActive = true
            });

            cache.Verify(c => c.Remove($"Product:{productId}"), Times.Once);
        }

        private static AppDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=EvangelionERPV2_ProductRepositoryTests;Trusted_Connection=True;")
                .Options;

            return new AppDbContext(options);
        }
    }
}
