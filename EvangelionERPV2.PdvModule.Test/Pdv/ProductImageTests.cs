using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Services;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EvangelionERPV2.PdvModule.Test.Pdv;

/// <summary>
/// The feed a till pulls product pictures from.
/// </summary>
public class ProductImageTests : IDisposable
{
    private readonly PdvTestContext _database = new();
    private readonly Guid _enterpriseId = Guid.NewGuid();

    public void Dispose() => _database.Dispose();

    /// <summary>
    /// Mirrors Task 1's contract exactly: an empty base64 and a null hash for a product with no
    /// usable picture, and a made-up thumbnail for one that has an address to resolve.
    /// </summary>
    private static Mock<IProductService<Product>> ProductServiceStub()
    {
        var mock = new Mock<IProductService<Product>>();
        mock.Setup(service => service.GetThumbnailBase64Async(It.IsAny<string?>()))
            .ReturnsAsync((string? pictureAddress) => pictureAddress == null
                ? (string.Empty, (string?)null)
                : ("QUJD", "sha256:abc"));
        return mock;
    }

    private static PdvService CreateService(AppDbContext context) =>
        new(context,
            new Repository<CashSession>(context),
            new Repository<CashMovement>(context),
            new Repository<PdvSale>(context),
            new Repository<PdvSalePayment>(context),
            new Mock<IOrderService<Order>>().Object,
            ProductServiceStub().Object,
            new Repository<PdvCharge>(context),
            PixProviderStubs.Object());

    /// <summary>
    /// Seeds products with strictly increasing <c>ChangedAt</c> values. <see cref="BaseEntity"/>
    /// defaults <c>UpdatedAt</c> to <see cref="DateTime.MinValue"/> rather than null, so it is set
    /// to null explicitly here — the same "never edited" state <c>GetProductImagesAsync</c> falls
    /// back from onto <c>CreatedAt</c>.
    /// </summary>
    private async Task SeedAsync(AppDbContext context, Guid enterpriseId, int withPictures, int withoutPictures)
    {
        var baseTime = DateTime.UtcNow;
        var offset = 0;

        for (var i = 0; i < withPictures; i++)
        {
            context.Product.Add(new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = $"Product {offset}",
                PictureAdress = $"pictures/{offset}.jpg",
                CreatedAt = baseTime.AddSeconds(offset),
                UpdatedAt = null
            });
            offset++;
        }

        for (var i = 0; i < withoutPictures; i++)
        {
            context.Product.Add(new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = $"Product {offset}",
                PictureAdress = null,
                CreatedAt = baseTime.AddSeconds(offset),
                UpdatedAt = null
            });
            offset++;
        }

        await context.SaveChangesAsync();
    }

    private async Task<PdvProductImagePageDTO> ImagesFor(
        int productsWithPictures, int productsWithout, int pageSize = 500)
    {
        var context = _database.CreateContext();
        await SeedAsync(context, _enterpriseId, productsWithPictures, productsWithout);

        var service = CreateService(context);
        return await service.GetProductImagesAsync(since: null, _enterpriseId, pageSize);
    }

    private async Task<PdvProductImagePageDTO> ImagesSince(DateTime cursor)
    {
        var context = _database.CreateContext();
        var service = CreateService(context);
        return await service.GetProductImagesAsync(cursor, _enterpriseId, pageSize: 500);
    }

    private async Task<PdvProductImagePageDTO> ImagesForOtherTenant()
    {
        var context = _database.CreateContext();
        await SeedAsync(context, _enterpriseId, withPictures: 0, withoutPictures: 1);

        var service = CreateService(context);
        return await service.GetProductImagesAsync(since: null, Guid.NewGuid(), pageSize: 500);
    }

    [Fact]
    public async Task AProductWithNoPictureIsReturnedWithANullHash()
    {
        var page = await ImagesFor(productsWithPictures: 0, productsWithout: 1);

        var item = Assert.Single(page.Items);
        Assert.Null(item.Hash);
        Assert.Equal(string.Empty, item.ThumbnailBase64);
    }

    [Fact]
    public async Task AProductWithAPictureCarriesItsHash()
    {
        var page = await ImagesFor(productsWithPictures: 1, productsWithout: 0);

        var item = Assert.Single(page.Items);
        Assert.Equal("sha256:abc", item.Hash);
        Assert.Equal("QUJD", item.ThumbnailBase64);
    }

    [Fact]
    public async Task ThePageIsCappedAtFifty()
    {
        var page = await ImagesFor(productsWithPictures: 0, productsWithout: 60, pageSize: 500);

        Assert.Equal(50, page.Items.Count());
        Assert.True(page.HasMore);

        Assert.Equal(page.Items.Last().ChangedAt, page.NextCursor);
    }

    [Fact]
    public async Task EqualTimestampsAreNotSplitAcrossCursorPages()
    {
        var context = _database.CreateContext();
        var changedAt = DateTime.UtcNow;

        for (var index = 0; index < 60; index++)
        {
            context.Product.Add(new Product
            {
                Id = Guid.NewGuid(),
                EnterpriseId = _enterpriseId,
                Name = $"Product {index}",
                CreatedAt = changedAt,
                UpdatedAt = null
            });
        }

        await context.SaveChangesAsync();
        var service = CreateService(context);

        var catalog = await service.GetCatalogAsync(null, _enterpriseId, pageSize: 50);
        var images = await service.GetProductImagesAsync(null, _enterpriseId, pageSize: 50);

        Assert.Equal(60, catalog.Items.Count());
        Assert.False(catalog.HasMore);
        Assert.Null(catalog.NextCursor);
        Assert.Equal(60, images.Items.Count());
        Assert.False(images.HasMore);
        Assert.Null(images.NextCursor);
    }

    [Fact]
    public async Task AFinalPagesCursorIsNull()
    {
        var page = await ImagesFor(productsWithPictures: 0, productsWithout: 3);

        Assert.False(page.HasMore);
        Assert.NotEmpty(page.Items);

        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task TheCursorOnlyReturnsWhatChangedAfterIt()
    {
        var page = await ImagesFor(productsWithPictures: 0, productsWithout: 3);
        var cursor = page.Items.Max(x => x.ChangedAt);

        var second = await ImagesSince(cursor);

        Assert.Empty(second.Items);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task AnotherTenantsProductsAreNeverReturned()
    {
        var page = await ImagesForOtherTenant();

        Assert.Empty(page.Items);
    }
}
