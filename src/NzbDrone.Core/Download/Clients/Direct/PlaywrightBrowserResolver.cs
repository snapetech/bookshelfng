using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Download.Clients.Direct
{
    public sealed class PlaywrightBrowserResolver : IBrowserDownloadResolver
    {
        private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan LinkWaitTimeout = TimeSpan.FromSeconds(15);
        private static readonly HashSet<string> DirectBookFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".epub", ".kepub", ".mobi", ".azw3"
        };

        private readonly Logger _logger;

        public PlaywrightBrowserResolver(Logger logger)
        {
            _logger = logger;
        }

        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(new Microsoft.Playwright.BrowserTypeLaunchOptions()
                {
                    ExecutablePath = Environment.GetEnvironmentVariable("PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH"),
                    Headless = true,
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage"
                    }
                });

                await browser.CloseAsync();
                playwright.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Playwright browser is not available: {0}", ex.Message);
                return false;
            }
        }

        public async Task<string> TryResolveSlowDownloadUrlAsync(string infoUrl)
        {
            if (infoUrl.IsNullOrWhiteSpace())
            {
                return null;
            }

            Microsoft.Playwright.IPlaywright playwright = null;
            Microsoft.Playwright.IBrowser browser = null;

            try
            {
                playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                browser = await playwright.Chromium.LaunchAsync(new Microsoft.Playwright.BrowserTypeLaunchOptions()
                {
                    ExecutablePath = Environment.GetEnvironmentVariable("PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH"),
                    Headless = true,
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage"
                    }
                });

                var context = await browser.NewContextAsync(new Microsoft.Playwright.BrowserNewContextOptions()
                {
                    UserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
                });

                var page = await context.NewPageAsync();
                await page.GotoAsync(infoUrl, new Microsoft.Playwright.PageGotoOptions { Timeout = (float)NavigationTimeout.TotalMilliseconds, WaitUntil = Microsoft.Playwright.WaitUntilState.NetworkIdle });

                // Wait for download links to appear (DDoS challenge may need JS execution)
                try
                {
                    await page.WaitForSelectorAsync(
                        "a[href*='/slow_download/'], a[href*='/fast_download/'], a[href*='.pdf' i], a[href*='.epub' i], a[href*='.kepub' i], a[href*='.mobi' i], a[href*='.azw3' i]",
                        new Microsoft.Playwright.PageWaitForSelectorOptions { Timeout = (float)LinkWaitTimeout.TotalMilliseconds });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.Debug("Browser timed out or failed waiting for download links on {0}: {1}", Redact(infoUrl), ex.Message);
                }

                // Extract download URLs in priority order
                var slowUrl = await GetFirstHttpLinkAsync(page, "a[href*='/slow_download/']", infoUrl);
                if (slowUrl != null)
                {
                    _logger.Debug("Browser resolved slow download URL: {0}", Redact(slowUrl));
                    await CloseAsync(browser, playwright);
                    return slowUrl;
                }

                var fastUrl = await GetFirstHttpLinkAsync(page, "a[href*='/fast_download/']", infoUrl);
                if (fastUrl != null)
                {
                    _logger.Debug("Browser resolved fast download URL: {0}", Redact(fastUrl));
                    await CloseAsync(browser, playwright);
                    return fastUrl;
                }

                var directFileUrl = await GetFirstDirectBookFileUrlAsync(page, infoUrl);
                if (directFileUrl != null)
                {
                    _logger.Debug("Browser resolved direct file URL: {0}", Redact(directFileUrl));
                    await CloseAsync(browser, playwright);
                    return directFileUrl;
                }

                _logger.Debug("Browser could not find any download link on {0}", infoUrl);
                await CloseAsync(browser, playwright);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Browser download resolution failed for {0}: {1}", Redact(infoUrl), ex.Message);
                await CloseSafeAsync(browser, playwright);
                return null;
            }
        }

        private static async Task CloseAsync(Microsoft.Playwright.IBrowser browser, Microsoft.Playwright.IPlaywright playwright)
        {
            if (browser != null)
            {
                await browser.CloseAsync();
            }

            playwright?.Dispose();
        }

        private static async Task CloseSafeAsync(Microsoft.Playwright.IBrowser browser, Microsoft.Playwright.IPlaywright playwright)
        {
            try
            {
                await CloseAsync(browser, playwright);
            }
            catch
            {
                // Swallow cleanup errors
            }
        }

        private static async Task<string> GetFirstHttpLinkAsync(Microsoft.Playwright.IPage page, string selector, string infoUrl)
        {
            var links = page.Locator(selector);
            var count = await links.CountAsync();

            for (var index = 0; index < count; index++)
            {
                var href = await links.Nth(index).GetAttributeAsync("href");
                if (TryResolveHttpUri(infoUrl, href, out var uri))
                {
                    return uri.AbsoluteUri;
                }
            }

            return null;
        }

        private static async Task<string> GetFirstDirectBookFileUrlAsync(Microsoft.Playwright.IPage page, string infoUrl)
        {
            var links = page.Locator("a[href]");
            var count = await links.CountAsync();

            for (var index = 0; index < count; index++)
            {
                var href = await links.Nth(index).GetAttributeAsync("href");
                if (TryResolveHttpUri(infoUrl, href, out var uri) && DirectBookFileExtensions.Contains(System.IO.Path.GetExtension(uri.AbsolutePath)))
                {
                    return uri.AbsoluteUri;
                }
            }

            return null;
        }

        private static bool TryResolveHttpUri(string infoUrl, string href, out Uri uri)
        {
            uri = null;
            if (href.IsNullOrWhiteSpace() || !Uri.TryCreate(new Uri(infoUrl), href, out var resolved) ||
                (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            uri = resolved;
            return true;
        }

        private static string Redact(string url)
        {
            // Redact query parameters that might contain keys
            if (url == null)
            {
                return null;
            }

            var queryIndex = url.IndexOf('?');
            return queryIndex >= 0 ? url[..queryIndex] + "?[redacted]" : url;
        }
    }
}
