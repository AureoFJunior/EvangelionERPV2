using System.Reflection;
using EvangelionERPV2.BillsModule.Application.Services;

namespace EvangelionERPV2.BillsModule.Test
{
    /// <summary>
    /// BoletoNetCore emits the barcode as data:image/gif holding JPEG bytes; mail clients and
    /// strict renderers drop the mismatched image, so the MIME has to be corrected.
    /// </summary>
    public class BillHtmlImageMimeTests
    {
        private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
        private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
        private static readonly byte[] GifBytes = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00];

        [Fact]
        public void Normalize_WhenJpegIsLabelledAsGif_RelabelsAsJpeg()
        {
            var payload = Convert.ToBase64String(JpegBytes);

            var result = Normalize($"<img src=\"data:image/gif;base64,{payload}\" />");

            Assert.Contains($"data:image/jpeg;base64,{payload}", result);
            Assert.DoesNotContain("image/gif", result);
        }

        [Fact]
        public void Normalize_WhenLabelMatchesContent_LeavesHtmlUnchanged()
        {
            var html =
                $"<img src=\"data:image/png;base64,{Convert.ToBase64String(PngBytes)}\" />" +
                $"<img src=\"data:image/gif;base64,{Convert.ToBase64String(GifBytes)}\" />";

            Assert.Equal(html, Normalize(html));
        }

        [Fact]
        public void Normalize_WhenPayloadIsNotAKnownImage_LeavesHtmlUnchanged()
        {
            var html = $"<img src=\"data:image/gif;base64,{Convert.ToBase64String("not an image"u8.ToArray())}\" />";

            Assert.Equal(html, Normalize(html));
        }

        [Fact]
        public void Normalize_RewritesEveryMismatchedImage()
        {
            var payload = Convert.ToBase64String(JpegBytes);
            var html =
                $"<img src=\"data:image/gif;base64,{payload}\" />" +
                $"<img src=\"data:image/bmp;base64,{payload}\" />";

            var result = Normalize(html);

            Assert.Equal(2, result.Split("data:image/jpeg;base64,").Length - 1);
        }

        private static string Normalize(string html)
        {
            var method = typeof(BillsService).GetMethod(
                "NormalizeEmbeddedImageMimeTypes",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            return (string)method!.Invoke(null, [html])!;
        }
    }
}
