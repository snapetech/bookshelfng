using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using NzbDrone.Core.Indexers.DirectDownload;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.DirectDownload
{
    [TestFixture]
    public class DirectDownloadSettingsFixture : CoreTest
    {
        [Test]
        public void should_keep_mirror_priority_and_remove_duplicates()
        {
            var urls = DirectDownloadSettings.NormalizeUrls(new[]
            {
                " https://primary.example/books/ ",
                "https://backup.example/books",
                "https://PRIMARY.example/books"
            });

            Assert.That(urls, Is.EqualTo(new[]
            {
                "https://primary.example/books",
                "https://backup.example/books"
            }));
        }

        [TestCase("\"https://catalog.example/\\nhttps://mirror.example/\"")]
        [TestCase("[\"https://catalog.example/\",\"https://mirror.example/\"]")]
        public void should_read_legacy_string_and_array_url_settings(string urlsJson)
        {
            var settings = JsonSerializer.Deserialize<DirectDownloadSettings>("{\"Urls\":" + urlsJson + "}");

            Assert.That(settings.Urls.ToArray(), Is.EqualTo(new[]
            {
                "https://catalog.example",
                "https://mirror.example"
            }));
        }

        [Test]
        public void should_store_url_lists_as_ordered_arrays()
        {
            var settings = new DirectDownloadSettings
            {
                Urls = new[] { "https://primary.example", "https://backup.example" },
                ApiKey = "secret",
                EnableSlowFallback = false
            };

            var json = JsonSerializer.Serialize(settings);
            var restored = JsonSerializer.Deserialize<DirectDownloadSettings>(json);

            Assert.That(restored.Urls.ToArray(), Is.EqualTo(settings.Urls.ToArray()));
            Assert.That(json, Does.Contain("[\"https://primary.example\",\"https://backup.example\"]"));
        }

        [Test]
        public void should_require_a_download_method()
        {
            var settings = new DirectDownloadSettings
            {
                Urls = new[] { "https://catalog.example" }
            };

            Assert.That(settings.Validate().IsValid, Is.False);
        }

        [Test]
        public void should_allow_public_slow_downloads_without_an_api_key()
        {
            var settings = new DirectDownloadSettings
            {
                Urls = new[] { "https://catalog.example" },
                EnableSlowFallback = true
            };

            Assert.That(settings.Validate().IsValid, Is.True);
        }

        [Test]
        public void should_allow_api_downloads_without_browser_fallback()
        {
            var settings = new DirectDownloadSettings
            {
                Urls = new[] { "https://catalog.example" },
                ApiKey = "key"
            };

            Assert.That(settings.Validate().IsValid, Is.True);
        }

        [TestCase("file:///books.epub")]
        [TestCase("ftp://catalog.example/")]
        [TestCase("https://user:password@catalog.example/")]
        [TestCase("http://localhost/")]
        [TestCase("http://127.0.0.1/")]
        [TestCase("http://10.0.0.1/")]
        [TestCase("http://169.254.1.1/")]
        [TestCase("http://100.64.0.1/")]
        [TestCase("http://[::1]/")]
        [TestCase("http://[fc00::1]/")]
        [TestCase("http://[fe80::1]/")]
        public void should_reject_unsafe_source_urls(string url)
        {
            Assert.Throws<DirectDownloadProbeException>(() => DirectDownloadUrlSafety.NormalizeAndValidate(url));
        }

        [TestCase("https://8.8.8.8/catalog/")]
        [TestCase("https://[2606:4700:4700::1111]/catalog/")]
        public void should_allow_public_http_hosts(string url)
        {
            Assert.That(DirectDownloadUrlSafety.NormalizeAndValidate(url).AbsoluteUri, Is.EqualTo(url));
        }
    }
}
