using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Services;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Moq;
using SkiaSharp;
using Xunit;

namespace EvangelionERPV2.ProductModule.Test
{
    // SPEC-erp-products-images-limen PI-1: a photo never costs the product, and a photo can be removed.
    public class ProductPictureLifecycleTests : IDisposable
    {
        private readonly StockLedgerTestContext _database = new();
        private readonly Guid _enterprise = Guid.NewGuid();

        public void Dispose() => _database.Dispose();

        private static string Jpeg(int width, int height)
        {
            using var bitmap = new SKBitmap(width, height);
            bitmap.Erase(SKColors.White);
            using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
            return Convert.ToBase64String(data.ToArray());
        }

        private Product NewProduct(Guid? group = null) =>
            new("Caneca", "Caneca de porcelana", 39.9m, 0, false, false, null, _enterprise)
            {
                Id = Guid.NewGuid(),
                IsActive = true,
                VariantGroupId = group
            };

        [Fact]
        public async Task Create_WithAPhotoTooSmall_SavesOneProductAndSaysWhy()
        {
            var request = new ProductPicture { Product = NewProduct(), File = Jpeg(300, 300) };

            var created = await CreateService(_database.CreateContext()).CreateAsync(request);

            Assert.Equal("A foto precisa ter pelo menos 400 px no lado menor (esta tem 300×300).", request.PictureError);
            using var fresh = _database.CreateContext();
            var saved = Assert.Single(fresh.Product.Where(p => p.EnterpriseId == _enterprise));
            Assert.Equal(created.Id, saved.Id);
            Assert.True(string.IsNullOrEmpty(saved.PictureAdress));
            Assert.Null(saved.PictureWidth);
        }

        [Fact]
        public async Task Create_WhenStorageFails_SavesTheProductAndSaysThePhotoWasNotSent()
        {
            var request = new ProductPicture { Product = NewProduct(), File = Jpeg(800, 800) };

            await CreateService(_database.CreateContext()).CreateAsync(request);

            Assert.Equal("O produto foi salvo, mas a foto não foi enviada. Tente enviá-la de novo.", request.PictureError);
            using var fresh = _database.CreateContext();
            Assert.True(string.IsNullOrEmpty(Assert.Single(fresh.Product.Where(p => p.EnterpriseId == _enterprise)).PictureAdress));
        }

        [Fact]
        public async Task Create_WithoutAPhoto_HasNoPictureError()
        {
            var request = new ProductPicture { Product = NewProduct() };

            await CreateService(_database.CreateContext()).CreateAsync(request);

            Assert.Null(request.PictureError);
        }

        [Fact]
        public async Task DeletePicture_ClearsTheAddressAndSize()
        {
            var product = NewProduct();
            product.PictureAdress = SharedFunctions.EnsureEncryptedAddress("products/caneca.jpg");
            product.PictureWidth = 1200;
            product.PictureHeight = 900;
            using (var seed = _database.CreateContext())
            {
                seed.Product.Add(product);
                await seed.SaveChangesAsync();
            }

            var context = _database.CreateContext();
            var tracked = context.Product.Single(p => p.Id == product.Id);
            Assert.True(await CreateService(context).DeletePictureAsync(tracked));

            using var fresh = _database.CreateContext();
            var saved = fresh.Product.Single(p => p.Id == product.Id);
            Assert.Null(saved.PictureAdress);
            Assert.Null(saved.PictureWidth);
            Assert.Null(saved.PictureHeight);
        }

        [Fact]
        public async Task DeletePicture_OnAProductWithoutOne_IsFalse()
        {
            var product = NewProduct();
            using (var seed = _database.CreateContext())
            {
                seed.Product.Add(product);
                await seed.SaveChangesAsync();
            }

            var context = _database.CreateContext();
            Assert.False(await CreateService(context).DeletePictureAsync(context.Product.Single(p => p.Id == product.Id)));
        }

        [Fact]
        public async Task DeleteFamilyPicture_RemovesOnlyThisTenantsFamilyPhoto()
        {
            var group = Guid.NewGuid();
            using (var seed = _database.CreateContext())
            {
                seed.ProductVariantGroup.Add(new ProductVariantGroup
                {
                    Id = group,
                    EnterpriseId = _enterprise,
                    PictureAdress = SharedFunctions.EnsureEncryptedAddress("product-families/caneca.jpg"),
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
                await seed.SaveChangesAsync();
            }

            var service = CreateService(_database.CreateContext());
            Assert.False(await service.DeleteFamilyPictureAsync(Guid.NewGuid(), group));
            Assert.True(await service.DeleteFamilyPictureAsync(_enterprise, group));
            Assert.False(await service.DeleteFamilyPictureAsync(_enterprise, group));

            using var fresh = _database.CreateContext();
            Assert.Null(fresh.ProductVariantGroup.Single(g => g.Id == group).PictureAdress);
        }

        [Fact]
        public async Task SetFamilyPicture_WithAPhotoTooSmall_IsRefusedBeforeStorage()
        {
            var group = Guid.NewGuid();
            using (var seed = _database.CreateContext())
            {
                seed.Product.Add(NewProduct(group));
                await seed.SaveChangesAsync();
            }

            var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                CreateService(_database.CreateContext()).SetFamilyPictureAsync(_enterprise, group, Jpeg(640, 200)));

            Assert.Equal("A foto precisa ter pelo menos 400 px no lado menor (esta tem 640×200).", error.Message);
        }

        private static ProductService CreateService(AppDbContext context)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AWSSettings:SecretName"] = "test-secret" })
                .Build();

            var secretsManager = new Mock<IAmazonSecretsManager>();
            secretsManager.Setup(s => s.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse { SecretString = "{\"access-key-id\":\"test\",\"secret-access-key\":\"test\"}" });

            return new ProductService(
                new Repository<Product>(context),
                configuration,
                new AWSKMSKeyProvider(secretsManager.Object, configuration),
                new Mock<IProductReportGeneratorService>().Object,
                context);
        }
    }
}
