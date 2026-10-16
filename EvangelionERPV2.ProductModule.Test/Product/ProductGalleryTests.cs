using Amazon.S3;
using Amazon.S3.Model;
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
using System.Reflection;
using Xunit;

namespace EvangelionERPV2.ProductModule.Test
{
    // SPEC-erp-products-images-limen PI-3: up to eight photos per row, position 0 mirrored onto the product.
    public class ProductGalleryTests : IDisposable
    {
        private readonly StockLedgerTestContext _database = new();
        private readonly Guid _enterprise = Guid.NewGuid();
        private readonly Mock<IAmazonS3> _s3 = new();
        private readonly List<string> _puts = new();
        private readonly List<string> _deletes = new();
        private readonly Product _product;

        public ProductGalleryTests()
        {
            var key = typeof(SharedFunctions).GetField("_encryptionKey", BindingFlags.NonPublic | BindingFlags.Static);
            if (string.IsNullOrWhiteSpace(key?.GetValue(null) as string))
                key?.SetValue(null, Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));

            _s3.Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
                .Callback<PutObjectRequest, CancellationToken>((r, _) => _puts.Add(r.Key))
                .ReturnsAsync(new PutObjectResponse());
            _s3.Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
                .Callback<DeleteObjectRequest, CancellationToken>((r, _) => _deletes.Add(r.Key))
                .ReturnsAsync(new DeleteObjectResponse());

            _product = new Product("Caneca", "Caneca de porcelana", 39.9m, 1, false, false, null, _enterprise) { Id = Guid.NewGuid(), IsActive = true };
            using var seed = _database.CreateContext();
            seed.Product.Add(_product);
            seed.SaveChanges();
        }

        public void Dispose() => _database.Dispose();

        private static string Jpeg(int width, int height, SKColor? color = null)
        {
            using var bitmap = new SKBitmap(width, height);
            bitmap.Erase(color ?? SKColors.White);
            using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
            return Convert.ToBase64String(data.ToArray());
        }

        private ProductService Service(AppDbContext? context = null)
        {
            context ??= _database.CreateContext();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AWSSettings:SecretName"] = "test-secret",
                    ["AWSSettings:BucketProducttName"] = "test-product-bucket"
                })
                .Build();
            var secrets = new Mock<IAmazonSecretsManager>();
            secrets.Setup(s => s.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse { SecretString = "{\"access-key-id\":\"test\",\"secret-access-key\":\"test\"}" });

            var service = new ProductService(new Repository<Product>(context), configuration,
                new AWSKMSKeyProvider(secrets.Object, configuration), new Mock<IProductReportGeneratorService>().Object, context);
            typeof(ProductService).GetField("_s3Client", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, _s3.Object);
            return service;
        }

        private Product StoredProduct()
        {
            using var context = _database.CreateContext();
            return context.Product.Single(p => p.Id == _product.Id);
        }

        private async Task<List<ProductImage>> AddAsync(int count)
        {
            var added = new List<ProductImage>();
            for (var i = 0; i < count; i++)
                added.Add(await Service().AddImageAsync(StoredProduct(), Jpeg(400 + i * 100, 500)));
            return added;
        }

        [Fact]
        public async Task Add_AppendsInOrder_AndTheFirstBecomesTheProductsPicture()
        {
            var added = await AddAsync(3);

            var images = await Service().GetImagesAsync(_enterprise, _product.Id);
            Assert.Equal(added.Select(i => i.Id), images.Select(i => i.Id));
            Assert.Equal([0, 1, 2], images.Select(i => i.Position));
            var product = StoredProduct();
            Assert.Equal(images[0].Adress, product.PictureAdress);
            Assert.Equal((400, 500), (product.PictureWidth, product.PictureHeight));
            Assert.Equal(3, _puts.Count);
        }

        [Fact]
        public async Task Add_ANinthPhoto_IsRefusedBeforeStorage()
        {
            using (var seed = _database.CreateContext())
            {
                for (var i = 0; i < ProductService.MaxImagesPerProduct; i++)
                    seed.ProductImage.Add(new ProductImage { Id = Guid.NewGuid(), ProductId = _product.Id, EnterpriseId = _enterprise, Position = i, Adress = $"products/{i}", IsActive = true });
                await seed.SaveChangesAsync();
            }

            var error = await Assert.ThrowsAsync<ProductGalleryFullException>(() => Service().AddImageAsync(StoredProduct(), Jpeg(600, 600)));

            Assert.Equal("Cada produto pode ter até 8 fotos. Remova uma antes de adicionar outra.", error.Message);
            Assert.Empty(_puts);
        }

        [Fact]
        public async Task Add_APhotoTooSmall_IsRefusedBeforeStorage()
        {
            await Assert.ThrowsAsync<ArgumentException>(() => Service().AddImageAsync(StoredProduct(), Jpeg(399, 800)));

            Assert.Empty(_puts);
            Assert.Empty(await Service().GetImagesAsync(_enterprise, _product.Id));
        }

        [Fact]
        public async Task Add_WhenProductWasDeletedAfterTheCallerReadIt_IsRejectedAndUploadedFileIsRemoved()
        {
            var staleActiveProduct = StoredProduct();
            using (var delete = _database.CreateContext())
            {
                var product = await delete.Product.SingleAsync(p => p.Id == _product.Id);
                product.IsActive = false;
                await delete.SaveChangesAsync();
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => Service().AddImageAsync(staleActiveProduct, Jpeg(600, 600)));

            Assert.Single(_puts);
            Assert.Equal(_puts, _deletes);
            Assert.Empty(await Service().GetImagesAsync(_enterprise, _product.Id));
        }

        [Fact]
        public async Task Move_ReordersAndRenumbers_AndTheNewFirstBecomesThePicture()
        {
            var added = await AddAsync(3);

            var moved = await Service().MoveImageAsync(_enterprise, added[2].Id, 0);

            Assert.Equal([added[2].Id, added[0].Id, added[1].Id], moved!.Select(i => i.Id));
            Assert.Equal([0, 1, 2], (await Service().GetImagesAsync(_enterprise, _product.Id)).Select(i => i.Position));
            Assert.Equal((600, 500), (StoredProduct().PictureWidth, StoredProduct().PictureHeight));
        }

        [Fact]
        public async Task Move_PastTheEnd_LandsLast()
        {
            var added = await AddAsync(3);

            var moved = await Service().MoveImageAsync(_enterprise, added[0].Id, 7);

            Assert.Equal([added[1].Id, added[2].Id, added[0].Id], moved!.Select(i => i.Id));
        }

        [Fact]
        public async Task Remove_TheCover_PromotesTheNextAndDeletesItsFiles()
        {
            var added = await AddAsync(2);
            var coverKey = _puts[0];

            Assert.True(await Service().RemoveImageAsync(_enterprise, added[0].Id));

            var images = await Service().GetImagesAsync(_enterprise, _product.Id);
            Assert.Equal(added[1].Id, Assert.Single(images).Id);
            Assert.Equal(0, images[0].Position);
            Assert.Equal(images[0].Adress, StoredProduct().PictureAdress);
            Assert.Equal([coverKey, "thumb200/" + coverKey, "thumb480/" + coverKey], _deletes);
        }

        [Fact]
        public async Task Remove_TheLastPhoto_LeavesTheProductWithoutAPicture()
        {
            var added = await AddAsync(1);

            Assert.True(await Service().RemoveImageAsync(_enterprise, added[0].Id));

            var product = StoredProduct();
            Assert.Null(product.PictureAdress);
            Assert.Null(product.PictureWidth);
        }

        [Fact]
        public async Task AnotherTenant_CannotSeeMoveOrRemoveThePhotos()
        {
            var added = await AddAsync(2);
            var stranger = Guid.NewGuid();

            Assert.Empty(await Service().GetImagesAsync(stranger, _product.Id));
            Assert.Null(await Service().GetImageAsync(stranger, added[0].Id));
            Assert.Null(await Service().MoveImageAsync(stranger, added[1].Id, 0));
            Assert.False(await Service().RemoveImageAsync(stranger, added[0].Id));
            Assert.Equal(added.Select(i => i.Id), (await Service().GetImagesAsync(_enterprise, _product.Id)).Select(i => i.Id));
            Assert.Empty(_deletes);
        }

        [Fact]
        public async Task UploadPicture_ReplacesOnlyTheCover()
        {
            var added = await AddAsync(2);
            var context = _database.CreateContext();
            var product = context.Product.Single(p => p.Id == _product.Id);

            await Service(context).UpdatePictureAsync(new ProductPicture { Product = product, File = Jpeg(1000, 800) });

            var images = await Service().GetImagesAsync(_enterprise, _product.Id);
            Assert.Equal(added.Select(i => i.Id), images.Select(i => i.Id));
            Assert.Equal((1000, 800), (images[0].Width, images[0].Height));
            Assert.Equal(images[0].Adress, StoredProduct().PictureAdress);
            Assert.Contains(SharedFunctions.EnsureDecryptedAddress(added[0].Adress), _deletes);
        }

        [Fact]
        public async Task UploadPicture_OnAProductWithoutAGallery_StartsOne()
        {
            var context = _database.CreateContext();
            var product = context.Product.Single(p => p.Id == _product.Id);

            await Service(context).UpdatePictureAsync(new ProductPicture { Product = product, File = Jpeg(800, 800) });

            var cover = Assert.Single(await Service().GetImagesAsync(_enterprise, _product.Id));
            Assert.Equal(0, cover.Position);
            Assert.Equal(cover.Adress, StoredProduct().PictureAdress);
        }

        [Fact]
        public async Task DeletePicture_RemovesTheCoverAndPromotesTheNext()
        {
            var added = await AddAsync(2);

            Assert.True(await Service().DeletePictureAsync(StoredProduct()));

            Assert.Equal(added[1].Id, Assert.Single(await Service().GetImagesAsync(_enterprise, _product.Id)).Id);
            Assert.Equal(added[1].Adress, StoredProduct().PictureAdress);
        }

        [Fact]
        public async Task Backfill_NormalisesAnOversizedUnsizedPhotoInPlace_AndIsIdempotent()
        {
            var store = new Dictionary<string, byte[]> { ["products/legacy"] = Convert.FromBase64String(Jpeg(3000, 1000)) };
            _s3.Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
                .Returns<GetObjectRequest, CancellationToken>((r, _) => store.TryGetValue(r.Key, out var bytes)
                    ? Task.FromResult(new GetObjectResponse { ResponseStream = new MemoryStream(bytes) })
                    : throw new AmazonS3Exception("missing") { StatusCode = System.Net.HttpStatusCode.NotFound });
            _s3.Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
                .Callback<PutObjectRequest, CancellationToken>((r, _) => { using var copy = new MemoryStream(); r.InputStream.CopyTo(copy); store[r.Key] = copy.ToArray(); _puts.Add(r.Key); })
                .ReturnsAsync(new PutObjectResponse());
            using (var seed = _database.CreateContext())
            {
                seed.ProductImage.Add(new ProductImage { Id = Guid.NewGuid(), ProductId = _product.Id, EnterpriseId = _enterprise, Position = 0, Adress = "products/legacy", IsActive = true });
                var product = seed.Product.Single(p => p.Id == _product.Id);
                product.PictureAdress = "products/legacy";
                await seed.SaveChangesAsync();
            }

            Assert.Equal(1, await Service().BackfillPicturesAsync(CancellationToken.None));

            using var bitmap = SKBitmap.Decode(store["products/legacy"]);
            Assert.Equal((2048, 683), (bitmap.Width, bitmap.Height));
            Assert.Contains("thumb200/products/legacy", store.Keys);
            Assert.Contains("thumb480/products/legacy", store.Keys);
            Assert.Equal((2048, 683), (StoredProduct().PictureWidth, StoredProduct().PictureHeight));
            var image = Assert.Single(await Service().GetImagesAsync(_enterprise, _product.Id));
            Assert.Equal((2048, 683), (image.Width, image.Height));

            _puts.Clear();
            Assert.Equal(0, await Service().BackfillPicturesAsync(CancellationToken.None));
            Assert.Empty(_puts);
        }
    }
}
