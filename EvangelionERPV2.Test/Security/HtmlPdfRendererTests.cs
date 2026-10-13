using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The renderer feeds Chromium a string we built, so every request the page then makes is an
    /// outbound request from the host. Only the schemes that cannot leave the process are allowed.
    /// </summary>
    public class HtmlPdfRendererTests
    {
        [Theory]
        [InlineData("data:image/png;base64,iVBORw0KGgo=")]
        [InlineData("DATA:image/jpeg;base64,/9j/4AAQ")]
        [InlineData("about:blank")]
        [InlineData("ABOUT:BLANK")]
        public void ShouldBlockBrowserRequest_ShouldAllowInlineAndBlankDocuments(string url)
        {
            Assert.False(HtmlPdfRenderer.ShouldBlockBrowserRequest(url));
        }

        [Theory]
        [InlineData("http://example.com/tracker.gif")]
        [InlineData("https://example.com/style.css")]
        [InlineData("file:///etc/passwd")]
        [InlineData("ftp://example.com/payload")]
        [InlineData("ws://example.com/socket")]
        [InlineData("wss://example.com/socket")]
        public void ShouldBlockBrowserRequest_ShouldBlockEveryOutboundScheme(string url)
        {
            Assert.True(HtmlPdfRenderer.ShouldBlockBrowserRequest(url));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-url")]
        [InlineData("//example.com/protocol-relative")]
        public void ShouldBlockBrowserRequest_ShouldBlockWhatItCannotParse(string? url)
        {
            Assert.True(HtmlPdfRenderer.ShouldBlockBrowserRequest(url));
        }
    }
}
