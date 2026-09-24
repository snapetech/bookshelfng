using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    [TestFixture]
    public class AdditionalMetadataSourcesFixture
    {
        [Test]
        public void should_enable_public_catalogs_by_default()
        {
            GetEnabled(null)
                .Should().BeEquivalentTo("loc", "gutendex");
        }

        [Test]
        public void should_enable_google_books_by_default_when_an_api_key_is_configured()
        {
            GetEnabled(null, "api-key")
                .Should().BeEquivalentTo("loc", "gutendex", "googlebooks");
        }

        [Test]
        public void should_enable_europeana_by_default_when_an_api_key_is_configured()
        {
            GetEnabled(null, null, "api-key")
                .Should().BeEquivalentTo("loc", "gutendex", "europeana");
        }

        [Test]
        public void should_allow_an_explicit_empty_value_to_disable_additional_sources()
        {
            GetEnabled(string.Empty, "api-key", "europeana-key")
                .Should().BeEmpty();
        }

        [Test]
        public void should_respect_an_explicit_provider_list()
        {
            GetEnabled("apify-goodreads; LOC", "api-key", "europeana-key", "actor", "token")
                .Should().BeEquivalentTo("apify-goodreads", "loc");
        }

        [Test]
        public void should_allow_saved_settings_to_replace_managed_compose_defaults()
        {
            AdditionalMetadataSources.GetEnabledSources(
                    "loc,gutendex,googlebooks,europeana",
                    "ndl,internetarchive",
                    true,
                    "google-key",
                    "europeana-key",
                    null,
                    null)
                .Should().BeEquivalentTo("ndl", "internetarchive");
        }

        [Test]
        public void should_keep_custom_environment_source_lists_authoritative()
        {
            AdditionalMetadataSources.GetEnabledSources(
                    "ndl",
                    "internetarchive",
                    true,
                    null,
                    null,
                    null,
                    null)
                .Should().BeEquivalentTo("ndl");
        }

        [Test]
        public void should_hide_keyed_sources_from_runtime_when_credentials_are_missing()
        {
            AdditionalMetadataSources.GetEnabledSources(
                    "googlebooks,europeana,apify-goodreads",
                    null,
                    false,
                    null,
                    null,
                    "actor",
                    null)
                .Should().BeEmpty();
        }

        [Test]
        public void should_ignore_unsupported_source_names()
        {
            GetEnabled("googlebooks,unknown-provider", "api-key")
                .Should().BeEquivalentTo("googlebooks");
        }

        private static System.Collections.Generic.HashSet<string> GetEnabled(
            string environmentSources,
            string googleBooksApiKey = null,
            string europeanaApiKey = null,
            string apifyActor = null,
            string apifyToken = null)
        {
            return AdditionalMetadataSources.GetEnabledSources(
                environmentSources,
                null,
                false,
                googleBooksApiKey,
                europeanaApiKey,
                apifyActor,
                apifyToken);
        }
    }
}
