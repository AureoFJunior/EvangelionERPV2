using System.Net;
using System.Threading;
using Amazon.S3;
using Amazon.S3.Model;
using EvangelionERPV2.ProductModule.Application.Services;
using Moq;
using SkiaSharp;
using Xunit;

namespace EvangelionERPV2.ProductModule.Test;

/// <summary>
/// Shrinking a product photo to something a till can hold thousands of.
/// </summary>
/// <remarks>
/// The resize is pure and separable from S3, so it is tested without touching AWS. What S3 does
/// with the result is Task 2's problem.
/// </remarks>
public class ThumbnailTests
{
    private static byte[] Png(int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Red);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void ALargeImageIsShrunkToTheLongEdge()
    {
        var thumb = ProductService.ResizeToThumbnail(Png(1200, 600), 200);

        using var decoded = SKBitmap.Decode(thumb);
        Assert.Equal(200, decoded.Width);
        Assert.Equal(100, decoded.Height);
    }

    [Fact]
    public void APortraitImageIsShrunkOnItsLongEdgeToo()
    {
        var thumb = ProductService.ResizeToThumbnail(Png(600, 1200), 200);

        using var decoded = SKBitmap.Decode(thumb);
        Assert.Equal(100, decoded.Width);
        Assert.Equal(200, decoded.Height);
    }

    [Fact]
    public void AnImageSmallerThanTheTargetIsLeftAlone()
    {
        // Upscaling costs bytes and adds nothing: the till renders at 56 px either way.
        var original = Png(120, 90);

        var thumb = ProductService.ResizeToThumbnail(original, 200);

        using var decoded = SKBitmap.Decode(thumb);
        Assert.Equal(120, decoded.Width);
        Assert.Equal(90, decoded.Height);
    }

    [Fact]
    public void TheThumbnailIsSubstantiallySmallerThanTheOriginal()
    {
        var original = Png(1600, 1200);

        var thumb = ProductService.ResizeToThumbnail(original, 200);

        Assert.True(thumb.Length < original.Length / 2,
            $"expected a real saving, got {thumb.Length} from {original.Length}");
    }

    [Fact]
    public void RubbishBytesReturnEmptyRatherThanThrowing()
    {
        // A corrupt object in the bucket must not take down a catalog page.
        Assert.Empty(ProductService.ResizeToThumbnail([1, 2, 3, 4], 200));
    }
}

/// <summary>
/// The read-through cache orchestration in <see cref="ProductService.BuildThumbnailAsync"/>,
/// against a mocked <see cref="IAmazonS3"/> so the cold-cache path (the one that was silently
/// broken) is exercised directly.
/// </summary>
public class ThumbnailOrchestrationTests
{
    private const string BucketName = "test-product-bucket";
    private const string PictureAddress = "products/photo.jpg";
    private const string ThumbnailKey = "thumb/products/photo.jpg";

    private static GetObjectResponse ResponseFor(byte[] bytes) => new()
    {
        ResponseStream = new MemoryStream(bytes)
    };

    private static AmazonS3Exception NotFound() => new("not found") { StatusCode = HttpStatusCode.NotFound };

    private static byte[] Png(int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Red);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task CacheHit_ReturnsCachedThumbnailWithoutTouchingTheOriginal()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync(It.Is<GetObjectRequest>(r => r.Key == ThumbnailKey), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResponseFor([1, 2, 3]));

        var (base64, hash) = await ProductService.BuildThumbnailAsync(s3.Object, BucketName, PictureAddress);

        Assert.Equal(Convert.ToBase64String([1, 2, 3]), base64);
        Assert.NotNull(hash);
        s3.Verify(x => x.GetObjectAsync(It.Is<GetObjectRequest>(r => r.Key == PictureAddress), It.IsAny<CancellationToken>()), Times.Never);
        s3.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CacheMiss_GeneratesAndWritesBackTheThumbnail()
    {
        var original = Png(1200, 600);
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync(It.Is<GetObjectRequest>(r => r.Key == ThumbnailKey), It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());
        s3.Setup(x => x.GetObjectAsync(It.Is<GetObjectRequest>(r => r.Key == PictureAddress), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResponseFor(original));
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutObjectResponse());

        var (base64, hash) = await ProductService.BuildThumbnailAsync(s3.Object, BucketName, PictureAddress);

        Assert.False(string.IsNullOrEmpty(base64));
        Assert.NotNull(hash);
        s3.Verify(x => x.PutObjectAsync(It.Is<PutObjectRequest>(r => r.Key == ThumbnailKey), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MissingOriginal_ReturnsEmptyRatherThanThrowing()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync(It.Is<GetObjectRequest>(r => r.Key == ThumbnailKey), It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());
        s3.Setup(x => x.GetObjectAsync(It.Is<GetObjectRequest>(r => r.Key == PictureAddress), It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());

        var (base64, hash) = await ProductService.BuildThumbnailAsync(s3.Object, BucketName, PictureAddress);

        Assert.Equal(string.Empty, base64);
        Assert.Null(hash);
    }

    [Fact]
    public async Task WriteBackFailure_StillReturnsTheThumbnailToTheCaller()
    {
        var original = Png(1200, 600);
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(x => x.GetObjectAsync(It.Is<GetObjectRequest>(r => r.Key == ThumbnailKey), It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());
        s3.Setup(x => x.GetObjectAsync(It.Is<GetObjectRequest>(r => r.Key == PictureAddress), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResponseFor(original));
        s3.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("write failed"));

        var (base64, hash) = await ProductService.BuildThumbnailAsync(s3.Object, BucketName, PictureAddress);

        Assert.False(string.IsNullOrEmpty(base64));
        Assert.NotNull(hash);
    }
}
