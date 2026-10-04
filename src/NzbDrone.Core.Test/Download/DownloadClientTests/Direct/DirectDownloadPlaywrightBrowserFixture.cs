using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Download.Clients.Direct;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.Direct
{
    [TestFixture]
    [Category("Playwright")]
    public class DirectDownloadPlaywrightBrowserFixture : CoreTest
    {
        [Test]
        public async Task should_resolve_a_slow_download_link_rendered_by_javascript_in_headless_chromium()
        {
            const string page = """
                <!doctype html>
                <html>
                  <body>
                    <a href="/fast_download/fast-result">Fast download</a>
                    <script>
                      setTimeout(() => {
                        const link = document.createElement('a');
                        link.href = '/slow_download/slow-result?token=download-token';
                        link.textContent = 'Slow download';
                        document.body.appendChild(link);
                      }, 75);
                    </script>
                  </body>
                </html>
                """;

            await using var server = new LocalHttpServer(page);
            var resolver = new PlaywrightBrowserResolver(TestLogger);

            await EnsureBrowserAvailableAsync(resolver);

            var resolvedUrl = await resolver.TryResolveSlowDownloadUrlAsync(server.RootUrl + "md5/book-id?key=page-token").WaitAsync(TimeSpan.FromSeconds(30));

            Assert.That(resolvedUrl, Is.EqualTo(server.RootUrl + "slow_download/slow-result?token=download-token"));
        }

        [Test]
        public async Task should_resolve_a_direct_book_file_link()
        {
            const string page = """
                <!doctype html>
                <html><body><a href="https://files.example.test/books/sample.epub">Download EPUB</a></body></html>
                """;

            await using var server = new LocalHttpServer(page);
            var resolver = new PlaywrightBrowserResolver(TestLogger);

            await EnsureBrowserAvailableAsync(resolver);

            var resolvedUrl = await resolver.TryResolveSlowDownloadUrlAsync(server.RootUrl + "md5/book-id").WaitAsync(TimeSpan.FromSeconds(30));

            Assert.That(resolvedUrl, Is.EqualTo("https://files.example.test/books/sample.epub"));
        }

        [Test]
        public async Task should_resolve_a_relative_book_file_link()
        {
            const string page = """
                <!doctype html>
                <html><body><a href="/books/sample.epub?download=1">Download EPUB</a></body></html>
                """;

            await using var server = new LocalHttpServer(page);
            var resolver = new PlaywrightBrowserResolver(TestLogger);

            await EnsureBrowserAvailableAsync(resolver);

            var resolvedUrl = await resolver.TryResolveSlowDownloadUrlAsync(server.RootUrl + "md5/book-id").WaitAsync(TimeSpan.FromSeconds(30));

            Assert.That(resolvedUrl, Is.EqualTo(server.RootUrl + "books/sample.epub?download=1"));
        }

        private static async Task EnsureBrowserAvailableAsync(PlaywrightBrowserResolver resolver)
        {
            if (await resolver.IsAvailableAsync().WaitAsync(TimeSpan.FromSeconds(30)))
            {
                return;
            }

            if (string.Equals(Environment.GetEnvironmentVariable("DIRECT_DOWNLOAD_REQUIRE_PLAYWRIGHT"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail("Headless Chromium is required but could not be launched.");
            }

            Assert.Ignore("Headless Chromium is not installed on this test host.");
        }

        private sealed class LocalHttpServer : IAsyncDisposable
        {
            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            private readonly byte[] _responseBody;
            private readonly Task _serverTask;

            public LocalHttpServer(string responseBody)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();

                var endpoint = (IPEndPoint)_listener.LocalEndpoint;
                RootUrl = $"http://127.0.0.1:{endpoint.Port}/";
                _responseBody = Encoding.UTF8.GetBytes(responseBody);
                _serverTask = ServeAsync();
            }

            public string RootUrl { get; }

            public async ValueTask DisposeAsync()
            {
                _cancellation.Cancel();
                _listener.Stop();

                try
                {
                    await _serverTask;
                }
                catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
                {
                }

                _cancellation.Dispose();
            }

            private async Task ServeAsync()
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
                    using var stream = client.GetStream();
                    var request = new StringBuilder();
                    var requestBuffer = new byte[1024];

                    while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    {
                        var count = await stream.ReadAsync(requestBuffer, _cancellation.Token);
                        if (count == 0)
                        {
                            break;
                        }

                        request.Append(Encoding.ASCII.GetString(requestBuffer, 0, count));
                    }

                    var headers = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {_responseBody.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers, _cancellation.Token);
                    await stream.WriteAsync(_responseBody, _cancellation.Token);
                    await stream.FlushAsync(_cancellation.Token);
                }
            }
        }
    }
}
