using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// The one headless-Chromium HTML-to-PDF renderer in the solution.
    /// </summary>
    public static class HtmlPdfRenderer
    {
        private static IBrowser? sharedBrowser;
        private static readonly SemaphoreSlim browserLock = new(1, 1);

        public static async Task<byte[]> RenderPdfAsync(string html)
        {
            var browser = await GetBrowserAsync();
            await using var page = await browser.NewPageAsync();
            await page.SetJavaScriptEnabledAsync(false);
            await page.SetRequestInterceptionAsync(true);
            page.Request += async (_, eventArgs) =>
            {
                if (ShouldBlockBrowserRequest(eventArgs.Request?.Url))
                {
                    await eventArgs.Request!.AbortAsync();
                    return;
                }

                await eventArgs.Request!.ContinueAsync();
            };
            await page.EmulateMediaTypeAsync(MediaType.Screen);
            await page.SetContentAsync(html, new SetContentOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Load }
            });
            var pdf = await page.PdfDataAsync(new PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                PreferCSSPageSize = true,
                MarginOptions = new MarginOptions
                {
                    Top = "0cm",
                    Bottom = "0cm",
                    Left = "0cm",
                    Right = "0cm"
                }
            });
            await page.CloseAsync();
            return pdf;
        }

        /// <summary>
        /// The document is rendered from a string we built; anything it then tries to fetch is a
        /// request out of the host, so only the schemes that cannot leave the process are allowed.
        /// </summary>
        public static bool ShouldBlockBrowserRequest(string? requestUrl)
        {
            var normalized = (requestUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return true;

            if (normalized.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                return false;

            if (normalized.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsedUri))
                return true;

            return parsedUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                   parsedUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                   parsedUri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) ||
                   parsedUri.Scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase) ||
                   parsedUri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase) ||
                   parsedUri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<IBrowser> GetBrowserAsync()
        {
            if (sharedBrowser != null)
                return sharedBrowser;

            await browserLock.WaitAsync();
            try
            {
                if (sharedBrowser != null)
                    return sharedBrowser;

                var executablePath = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    var browserCachePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "EvangelionERPV2",
                        "puppeteer-sharp");
                    Directory.CreateDirectory(browserCachePath);
                    if (!OperatingSystem.IsWindows())
                        File.SetUnixFileMode(browserCachePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

                    var fetcher = new BrowserFetcher(new BrowserFetcherOptions
                    {
                        Path = browserCachePath
                    });
                    executablePath = (await fetcher.DownloadAsync()).GetExecutablePath();
                }

                sharedBrowser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true,
                    ExecutablePath = executablePath,
                    Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"]
                });
            }
            finally
            {
                browserLock.Release();
            }

            return sharedBrowser;
        }
    }
}
