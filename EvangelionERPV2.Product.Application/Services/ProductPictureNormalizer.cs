using EvangelionERPV2.Shared.Utils;
using SkiaSharp;

namespace EvangelionERPV2.ProductModule.Application.Services
{
    public sealed record NormalizedPicture(byte[] Bytes, int Width, int Height);

    /// <summary>
    /// What every stored product photo goes through: EXIF orientation applied, long edge at most
    /// <see cref="MaxLongEdge"/>, re-encoded as JPEG (PNG when it really has transparency), which also
    /// drops the metadata. Throws <see cref="ArgumentException"/> with a pt-BR message the client shows.
    /// </summary>
    public static class ProductPictureNormalizer
    {
        public const int MaxLongEdge = 2048;
        public const int MinShortEdge = 400;

        /// <summary>A 12 MP phone photo is ~48 MB decoded; a header claiming more than this is refused unread.</summary>
        public const long MaxPixels = 40_000_000;
        private const int JpegQuality = 85;
        private const string Unreadable = "Não foi possível ler a imagem. Envie PNG, JPEG ou WebP.";

        public static NormalizedPicture FromBase64(string? payload)
        {
            byte[] bytes;
            try
            {
                using var stream = SharedFunctions.GetMemoryStreamFromBase64Payload(payload ?? string.Empty);
                bytes = stream.ToArray();
            }
            catch (ArgumentException)
            {
                throw new ArgumentException(Unreadable);
            }

            return Normalize(bytes);
        }

        public static NormalizedPicture Normalize(byte[] original)
        {
            using var codec = SKCodec.Create(new SKMemoryStream(original)) ?? throw new ArgumentException(Unreadable);

            var info = codec.Info;
            if ((long)info.Width * info.Height > MaxPixels)
                throw new ArgumentException($"A foto é grande demais: envie até {MaxPixels / 1_000_000} megapixels (esta tem {info.Width}×{info.Height}).");
            if (Math.Min(info.Width, info.Height) < MinShortEdge)
                throw new ArgumentException($"A foto precisa ter pelo menos {MinShortEdge} px no lado menor (esta tem {info.Width}×{info.Height}).");

            using var source = SKBitmap.Decode(codec, new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
                ?? throw new ArgumentException(Unreadable);

            var origin = codec.EncodedOrigin;
            var swapsAxes = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var orientedWidth = swapsAxes ? source.Height : source.Width;
            var orientedHeight = swapsAxes ? source.Width : source.Height;
            var scale = Math.Min(1.0, (double)MaxLongEdge / Math.Max(orientedWidth, orientedHeight));
            var width = Math.Max(1, (int)Math.Round(orientedWidth * scale));
            var height = Math.Max(1, (int)Math.Round(orientedHeight * scale));

            using var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(target))
            using (var image = SKImage.FromBitmap(source))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale((float)width / orientedWidth, (float)height / orientedHeight);
                canvas.Concat(OrientationMatrix(origin, source.Width, source.Height));
                canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }

            var keepsAlpha = info.AlphaType != SKAlphaType.Opaque && HasTransparentPixel(target);
            using var encoded = target.Encode(keepsAlpha ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, keepsAlpha ? 100 : JpegQuality);
            return new NormalizedPicture(encoded.ToArray(), width, height);
        }

        /// <summary>Maps the stored pixels (w × h) onto the upright picture the EXIF origin describes.</summary>
        private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity
        };

        private static bool HasTransparentPixel(SKBitmap bitmap)
        {
            var pixels = bitmap.GetPixelSpan();
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 255)
                    return true;
            }

            return false;
        }
    }
}
