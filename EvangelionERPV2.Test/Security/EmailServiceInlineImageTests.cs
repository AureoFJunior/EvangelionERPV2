using System.Reflection;
using EvangelionERPV2.EmailModule.Application.Services;
using MimeKit;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Gmail and Outlook drop data: image URIs, which blanked out the bill barcode. Embedded images
    /// have to travel as linked resources referenced by cid:.
    /// </summary>
    public class EmailServiceInlineImageTests
    {
        private static readonly string JpegPayload = Convert.ToBase64String(
            [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01]);

        [Fact]
        public void InlineDataUriImages_ReplacesEveryDataUriWithACidReference()
        {
            var bodyBuilder = new BodyBuilder();
            var html =
                $"<img src=\"data:image/jpeg;base64,{JpegPayload}\" />" +
                $"<img src=\"data:image/png;base64,{JpegPayload}\" />";

            var result = Inline(html, bodyBuilder);

            Assert.DoesNotContain("data:image", result);
            Assert.Equal(2, result.Split("src=\"cid:").Length - 1);
            Assert.Equal(2, bodyBuilder.LinkedResources.Count);
            Assert.All(bodyBuilder.LinkedResources, resource => Assert.False(string.IsNullOrWhiteSpace(resource.ContentId)));
        }

        [Fact]
        public void InlineDataUriImages_LinksTheCidToTheResourceContentId()
        {
            var bodyBuilder = new BodyBuilder();

            var result = Inline($"<img src=\"data:image/jpeg;base64,{JpegPayload}\" />", bodyBuilder);

            var contentId = bodyBuilder.LinkedResources.Single().ContentId;
            Assert.Contains($"cid:{contentId}", result);
        }

        [Fact]
        public void InlineDataUriImages_WithoutImages_LeavesTheBodyAlone()
        {
            var bodyBuilder = new BodyBuilder();
            const string html = "<p>Linha digitável: 03399.12347</p>";

            Assert.Equal(html, Inline(html, bodyBuilder));
            Assert.Empty(bodyBuilder.LinkedResources);
        }

        [Fact]
        public void InlineDataUriImages_WithCorruptPayload_KeepsTheOriginalMarkup()
        {
            var bodyBuilder = new BodyBuilder();
            const string html = "<img src=\"data:image/jpeg;base64,A\" />";

            Assert.Equal(html, Inline(html, bodyBuilder));
            Assert.Empty(bodyBuilder.LinkedResources);
        }

        private static string Inline(string html, BodyBuilder bodyBuilder)
        {
            var method = typeof(EmailService).GetMethod(
                "InlineDataUriImages",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            return (string)method!.Invoke(null, [html, bodyBuilder])!;
        }
    }
}
