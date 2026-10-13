using EvangelionERPV2.StorefrontModule.Application.DTOs;
using Moq;

namespace EvangelionERPV2.StorefrontModule.Test
{
    // SPEC-limen-design-alive §5.2/§6: a shop uploads its logo and cover; Limen gets the bytes and a
    // version that changes with every upload, so its image cache never serves a replaced one.
    public class StorefrontImageTests : IDisposable
    {
        private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 4, 5, 6];
        private readonly StorefrontTestContext _database = new();
        private readonly Dictionary<string, byte[]> _bucket = new();
        private readonly List<string> _deleted = new();
        private readonly List<string> _folders = new();
        private int _uploads;

        public StorefrontImageTests()
        {
            _database.ProductService
                .Setup(p => p.StoreImageAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<byte[]>()))
                .ReturnsAsync((string folder, Guid _, byte[] image) =>
                {
                    var address = $"enc:{folder}/{++_uploads}";
                    _folders.Add(folder);
                    _bucket[address] = image;
                    return address;
                });
            _database.ProductService
                .Setup(p => p.DeleteImageAsync(It.IsAny<string?>()))
                .Returns((string? address) =>
                {
                    if (address != null)
                    {
                        _deleted.Add(address);
                        _bucket.Remove(address);
                    }
                    return Task.CompletedTask;
                });
            _database.ProductService
                .Setup(p => p.GetPictureBase64Async(It.IsAny<string?>()))
                .ReturnsAsync((string? address) => address != null && _bucket.TryGetValue(address, out var bytes) ? Convert.ToBase64String(bytes) : string.Empty);
        }

        public void Dispose() => _database.Dispose();

        private async Task<Shared.Entities.Enterprise> ListedShopAsync(bool listed = true)
        {
            var shop = Build.ListedShop("aurora", listed: listed);
            await _database.SeedAsync(shop, Build.Fiscal(shop));
            return shop;
        }

        [Fact]
        public async Task Upload_StoresTheLogoAndPublishesAVersionLimenCanCacheOn()
        {
            var shop = await ListedShopAsync();
            var service = _database.CreateService();

            var listing = await service.SetListingImageAsync(shop.Id, StorefrontImageKind.Logo, Convert.ToBase64String(Png));

            Assert.Equal(["storefront-logos"], _folders);
            Assert.Matches("^[0-9a-f]{12}$", listing.LogoVersion);
            Assert.Null(listing.CoverVersion);
            var profile = await _database.CreateService().GetEnterpriseAsync(shop.StorefrontSlug!);
            Assert.Equal(listing.LogoVersion, profile!.LogoVersion);
            var summary = (await _database.CreateService().GetEnterprisesAsync(1, 10)).Items.Single();
            Assert.Equal(listing.LogoVersion, summary.LogoVersion);
            var image = await _database.CreateService().GetEnterpriseImageAsync(shop.StorefrontSlug!, StorefrontImageKind.Logo);
            Assert.Equal(Png, image!.Content);
            Assert.Equal("image/png", image.ContentType);
        }

        [Fact]
        public async Task Upload_AgainChangesTheVersionAndDeletesTheReplacedImage()
        {
            var shop = await ListedShopAsync();
            var first = await _database.CreateService().SetListingImageAsync(shop.Id, StorefrontImageKind.Cover, Convert.ToBase64String(Png));

            var second = await _database.CreateService().SetListingImageAsync(shop.Id, StorefrontImageKind.Cover, Convert.ToBase64String(Jpeg));

            Assert.NotEqual(first.CoverVersion, second.CoverVersion);
            Assert.Equal(["enc:storefront-covers/1"], _deleted);
            var image = await _database.CreateService().GetEnterpriseImageAsync(shop.StorefrontSlug!, StorefrontImageKind.Cover);
            Assert.Equal(Jpeg, image!.Content);
        }

        [Fact]
        public async Task Upload_KeepsLogoAndCoverApart()
        {
            var shop = await ListedShopAsync();
            await _database.CreateService().SetListingImageAsync(shop.Id, StorefrontImageKind.Logo, Convert.ToBase64String(Png));

            var listing = await _database.CreateService().SetListingImageAsync(shop.Id, StorefrontImageKind.Cover, Convert.ToBase64String(Jpeg));

            Assert.NotNull(listing.LogoVersion);
            Assert.NotNull(listing.CoverVersion);
            Assert.Equal(["storefront-logos", "storefront-covers"], _folders);
            Assert.Empty(_deleted);
        }

        public static TheoryData<string> NotAnAcceptedImage => new()
        {
            Convert.ToBase64String("<svg onload=alert(1)>"u8.ToArray()),
            Convert.ToBase64String([(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a']),
            "not base64 at all",
            "",
            Convert.ToBase64String([.. Png, .. new byte[5 * 1024 * 1024]]),
        };

        [Theory]
        [MemberData(nameof(NotAnAcceptedImage))]
        public async Task Upload_RefusesAnythingButPngJpegOrWebpUpTo5MbAndStoresNothing(string file)
        {
            var shop = await ListedShopAsync();

            var error = await Assert.ThrowsAsync<ArgumentException>(() =>
                _database.CreateService().SetListingImageAsync(shop.Id, StorefrontImageKind.Logo, file));

            Assert.Equal("A imagem deve ser PNG, JPEG ou WebP de até 5 MB.", error.Message);
            Assert.Empty(_folders);
        }

        [Fact]
        public async Task Upload_AcceptsWebp()
        {
            var shop = await ListedShopAsync();
            byte[] webp = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P', 7];

            var listing = await _database.CreateService().SetListingImageAsync(shop.Id, StorefrontImageKind.Logo, Convert.ToBase64String(webp));

            Assert.NotNull(listing.LogoVersion);
        }

        [Fact]
        public async Task Remove_ClearsTheVersionDeletesTheImageAndStopsServingIt()
        {
            var shop = await ListedShopAsync();
            await _database.CreateService().SetListingImageAsync(shop.Id, StorefrontImageKind.Logo, Convert.ToBase64String(Png));

            var listing = await _database.CreateService().RemoveListingImageAsync(shop.Id, StorefrontImageKind.Logo);

            Assert.Null(listing.LogoVersion);
            Assert.Equal(["enc:storefront-logos/1"], _deleted);
            Assert.Null(await _database.CreateService().GetEnterpriseImageAsync(shop.StorefrontSlug!, StorefrontImageKind.Logo));
        }

        [Fact]
        public async Task Remove_WithNothingUploaded_DeletesNothing()
        {
            var shop = await ListedShopAsync();

            var listing = await _database.CreateService().RemoveListingImageAsync(shop.Id, StorefrontImageKind.Cover);

            Assert.Null(listing.CoverVersion);
            Assert.Empty(_deleted);
        }

        [Fact]
        public async Task AnUnlistedShopsImage_IsShownToItsOwnerButNotToLimen()
        {
            var shop = await ListedShopAsync(listed: false);
            await _database.CreateService().SetListingImageAsync(shop.Id, StorefrontImageKind.Logo, Convert.ToBase64String(Png));

            Assert.Null(await _database.CreateService().GetEnterpriseImageAsync(shop.StorefrontSlug!, StorefrontImageKind.Logo));
            var own = await _database.CreateService().GetListingImageAsync(shop.Id, StorefrontImageKind.Logo);
            Assert.Equal(Png, own!.Content);
        }
    }
}
