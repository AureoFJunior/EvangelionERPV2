using EvangelionERPV2.Shared.Entities;
using Moq;

namespace EvangelionERPV2.StorefrontModule.Test
{
    // SPEC-erp-products-images-limen PI-3: Limen gets the row's gallery in order, and only for a published row of a listed shop.
    public class StorefrontGalleryTests : IDisposable
    {
        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 4, 5, 6];
        private readonly StorefrontTestContext _database = new();

        public StorefrontGalleryTests()
        {
            _database.ProductService
                .Setup(p => p.GetPictureBase64Async(It.IsAny<string?>()))
                .ReturnsAsync((string? address) => address == null ? string.Empty : Convert.ToBase64String(Jpeg));
        }

        public void Dispose() => _database.Dispose();

        private static ProductImage Image(Product product, int position, bool active = true) => new()
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            EnterpriseId = product.EnterpriseId!.Value,
            Position = position,
            Adress = $"products/{product.Id}/{position}",
            IsActive = active
        };

        [Fact]
        public async Task Detail_ListsTheGalleryInPositionOrder()
        {
            var shop = Build.ListedShop("aurora");
            var item = Build.Item(shop, "Caneca", 39.9m);
            var second = Image(item, 1);
            var first = Image(item, 0);
            var removed = Image(item, 2, active: false);
            await _database.SeedAsync(shop, Build.Fiscal(shop), item, second, first, removed);

            var detail = await _database.CreateService().GetProductAsync(item.Id);

            Assert.Equal([first.Id, second.Id], detail!.ImageIds);
        }

        [Fact]
        public async Task Detail_WithoutAGallery_HasNoImageIds()
        {
            var shop = Build.ListedShop("aurora");
            var item = Build.Item(shop, "Caneca", 39.9m);
            await _database.SeedAsync(shop, Build.Fiscal(shop), item);

            Assert.Empty((await _database.CreateService().GetProductAsync(item.Id))!.ImageIds!);
        }

        [Fact]
        public async Task ProductImage_ServesAPublishedRowsPhoto()
        {
            var shop = Build.ListedShop("aurora");
            var item = Build.Item(shop, "Caneca", 39.9m);
            var image = Image(item, 0);
            await _database.SeedAsync(shop, Build.Fiscal(shop), item, image);

            var picture = await _database.CreateService().GetProductImageAsync(image.Id);

            Assert.Equal(Jpeg, picture!.Content);
            Assert.Equal("image/jpeg", picture.ContentType);
        }

        [Fact]
        public async Task ProductImage_OfAnUnpublishedRowOrUnlistedShopOrRemovedPhoto_IsNotServed()
        {
            var shop = Build.ListedShop("aurora");
            var hidden = Build.ListedShop("sombra", listed: false);
            var draft = Build.Item(shop, "Rascunho", 10m, published: false);
            var unlisted = Build.Item(hidden, "Oculto", 10m);
            var live = Build.Item(shop, "Caneca", 10m);
            var draftImage = Image(draft, 0);
            var unlistedImage = Image(unlisted, 0);
            var removedImage = Image(live, 0, active: false);
            await _database.SeedAsync(shop, Build.Fiscal(shop), hidden, Build.Fiscal(hidden), draft, unlisted, live, draftImage, unlistedImage, removedImage);

            var service = _database.CreateService();
            Assert.Null(await service.GetProductImageAsync(draftImage.Id));
            Assert.Null(await service.GetProductImageAsync(unlistedImage.Id));
            Assert.Null(await service.GetProductImageAsync(removedImage.Id));
            Assert.Null(await service.GetProductImageAsync(Guid.NewGuid()));
        }
    }
}
