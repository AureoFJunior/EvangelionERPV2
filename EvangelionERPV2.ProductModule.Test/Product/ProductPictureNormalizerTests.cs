using EvangelionERPV2.ProductModule.Application.Services;
using SkiaSharp;

namespace EvangelionERPV2.ProductModule.Test
{
    public class ProductPictureNormalizerTests
    {
        private static byte[] Encode(int width, int height, SKEncodedImageFormat format, Action<SKCanvas>? draw = null, SKColor? fill = null)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(fill ?? SKColors.White);
                draw?.Invoke(canvas);
            }

            using var data = bitmap.Encode(format, 90);
            return data.ToArray();
        }

        /// <summary>The JPEG with an APP1 Exif segment carrying only an Orientation tag, right after SOI.</summary>
        private static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
        {
            byte[] tiff =
            [
                (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
                0x01, 0x00,
                0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, (byte)orientation, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            ];
            var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
            var length = payload.Length + 2;
            byte[] segment = [0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload];
            return [.. jpeg[..2], .. segment, .. jpeg[2..]];
        }

        private static SKBitmap Decode(byte[] bytes) => SKBitmap.Decode(bytes);

        [Fact]
        public void ALargePhoto_IsShrunkToTheLongEdgeLimitAsJpeg()
        {
            var result = ProductPictureNormalizer.Normalize(Encode(4000, 3000, SKEncodedImageFormat.Jpeg));

            Assert.Equal((2048, 1536), (result.Width, result.Height));
            Assert.Equal([0xFF, 0xD8], result.Bytes[..2]);
            using var stored = Decode(result.Bytes);
            Assert.Equal((2048, 1536), (stored.Width, stored.Height));
        }

        [Fact]
        public void APhotoWithinTheLimit_KeepsItsSize()
        {
            var result = ProductPictureNormalizer.Normalize(Encode(900, 600, SKEncodedImageFormat.Png));

            Assert.Equal((900, 600), (result.Width, result.Height));
            Assert.Equal([0xFF, 0xD8], result.Bytes[..2]);
        }

        [Fact]
        public void APhotoUnder400OnTheShortEdge_IsRefusedWithItsSize()
        {
            var error = Assert.Throws<ArgumentException>(() => ProductPictureNormalizer.Normalize(Encode(1200, 399, SKEncodedImageFormat.Jpeg)));

            Assert.Equal("A foto precisa ter pelo menos 400 px no lado menor (esta tem 1200×399).", error.Message);
        }

        /// <summary>A PNG whose header claims <paramref name="width"/> × <paramref name="height"/> but carries almost no pixel data.</summary>
        private static byte[] PngClaiming(int width, int height)
        {
            static byte[] Chunk(string type, byte[] data)
            {
                var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
                uint crc = 0xFFFFFFFF;
                foreach (var b in typed)
                {
                    crc ^= b;
                    for (var k = 0; k < 8; k++)
                        crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
                }
                crc ^= 0xFFFFFFFF;
                return [.. BigEndian((uint)data.Length), .. typed, .. BigEndian(crc)];
            }
            static byte[] BigEndian(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

            using var compressed = new MemoryStream();
            using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(new byte[1024]);

            byte[] header = [.. BigEndian((uint)width), .. BigEndian((uint)height), 8, 6, 0, 0, 0];
            return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", header), .. Chunk("IDAT", compressed.ToArray()), .. Chunk("IEND", [])];
        }

        [Fact]
        public void APhotoClaimingMoreThan40Megapixels_IsRefusedBeforeItIsDecoded()
        {
            var bomb = PngClaiming(30000, 30000);
            Assert.True(bomb.Length < 2048);

            var error = Assert.Throws<ArgumentException>(() => ProductPictureNormalizer.Normalize(bomb));

            Assert.Equal("A foto é grande demais: envie até 40 megapixels (esta tem 30000×30000).", error.Message);
        }

        [Fact]
        public void BytesThatAreNotAnImage_AreRefused()
        {
            var error = Assert.Throws<ArgumentException>(() => ProductPictureNormalizer.Normalize("not an image at all"u8.ToArray()));

            Assert.StartsWith("Não foi possível ler a imagem", error.Message);
        }

        [Fact]
        public void APngWithTransparency_StaysPngAndKeepsIt()
        {
            var result = ProductPictureNormalizer.Normalize(Encode(600, 600, SKEncodedImageFormat.Png,
                canvas => canvas.DrawRect(0, 0, 300, 600, new SKPaint { Color = SKColors.Red }), fill: SKColors.Transparent));

            Assert.Equal([0x89, 0x50, 0x4E, 0x47], result.Bytes[..4]);
            using var stored = Decode(result.Bytes);
            Assert.Equal(0, stored.GetPixel(500, 300).Alpha);
            Assert.Equal(255, stored.GetPixel(100, 300).Alpha);
        }

        [Fact]
        public void AnOpaquePng_BecomesJpeg()
        {
            var result = ProductPictureNormalizer.Normalize(Encode(600, 600, SKEncodedImageFormat.Png, fill: SKColors.Blue));

            Assert.Equal([0xFF, 0xD8], result.Bytes[..2]);
        }

        [Fact]
        public void TheExifOrientation_IsAppliedAndTheMetadataDropped()
        {
            var sideways = WithExifOrientation(Encode(800, 400, SKEncodedImageFormat.Jpeg,
                canvas => canvas.DrawRect(0, 0, 400, 400, new SKPaint { Color = SKColors.Red }), fill: SKColors.Blue), orientation: 6);

            var result = ProductPictureNormalizer.Normalize(sideways);

            Assert.Equal((400, 800), (result.Width, result.Height));
            using var stored = Decode(result.Bytes);
            var top = stored.GetPixel(200, 100);
            var bottom = stored.GetPixel(200, 700);
            Assert.True(top.Red > 200 && top.Blue < 60, $"top should be red, was {top}");
            Assert.True(bottom.Blue > 200 && bottom.Red < 60, $"bottom should be blue, was {bottom}");
            Assert.DoesNotContain("Exif", System.Text.Encoding.ASCII.GetString(result.Bytes));
        }
    }
}
