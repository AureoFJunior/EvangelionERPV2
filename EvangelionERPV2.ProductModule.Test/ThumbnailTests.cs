using EvangelionERPV2.ProductModule.Application.Services;
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
