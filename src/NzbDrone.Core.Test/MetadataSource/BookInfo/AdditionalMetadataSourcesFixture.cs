using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.BookInfo;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    [TestFixture]
    public class AdditionalMetadataSourcesFixture
    {
        [Test]
        public void should_enable_public_loc_by_default()
        {
            AdditionalMetadataSources.GetEnabledSources(null, null)
                .Should().BeEquivalentTo("loc");
        }

        [Test]
        public void should_enable_google_books_by_default_when_an_api_key_is_configured()
        {
            AdditionalMetadataSources.GetEnabledSources(null, "api-key")
                .Should().BeEquivalentTo("loc", "googlebooks");
        }

        [Test]
        public void should_allow_an_explicit_empty_value_to_disable_additional_sources()
        {
            AdditionalMetadataSources.GetEnabledSources(string.Empty, "api-key")
                .Should().BeEmpty();
        }

        [Test]
        public void should_respect_an_explicit_provider_list()
        {
            AdditionalMetadataSources.GetEnabledSources("apify-goodreads; LOC", "api-key")
                .Should().BeEquivalentTo("apify-goodreads", "loc");
        }
    }
}
