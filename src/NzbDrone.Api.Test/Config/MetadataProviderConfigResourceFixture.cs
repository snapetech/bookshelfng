using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Config;

namespace NzbDrone.Api.Test.Config
{
    [TestFixture]
    public class MetadataProviderConfigResourceFixture : TestBase
    {
        private string _previousHardcoverAuth;
        private string _previousHardcoverApiKey;
        private string _previousGoogleBooksKey;
        private string _previousEuropeanaKey;
        private string _previousApifyActor;
        private string _previousApifyToken;
        private string _previousApifyTemplate;
        private string _previousSources;

        [SetUp]
        public void Setup()
        {
            _previousHardcoverAuth = Environment.GetEnvironmentVariable("HARDCOVER_AUTH");
            _previousHardcoverApiKey = Environment.GetEnvironmentVariable("HARDCOVER_API_KEY");
            _previousGoogleBooksKey = Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY");
            _previousEuropeanaKey = Environment.GetEnvironmentVariable("EUROPEANA_API_KEY");
            _previousApifyActor = Environment.GetEnvironmentVariable("HARDCOVER_APIFY_GOODREADS_ACTOR");
            _previousApifyToken = Environment.GetEnvironmentVariable("HARDCOVER_APIFY_TOKEN");
            _previousApifyTemplate = Environment.GetEnvironmentVariable("HARDCOVER_APIFY_GOODREADS_INPUT_TEMPLATE");
            _previousSources = Environment.GetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES");

            Environment.SetEnvironmentVariable("HARDCOVER_AUTH", null);
            Environment.SetEnvironmentVariable("HARDCOVER_API_KEY", null);
            Environment.SetEnvironmentVariable("GOOGLE_BOOKS_API_KEY", null);
            Environment.SetEnvironmentVariable("EUROPEANA_API_KEY", null);
            Environment.SetEnvironmentVariable("HARDCOVER_APIFY_GOODREADS_ACTOR", null);
            Environment.SetEnvironmentVariable("HARDCOVER_APIFY_TOKEN", null);
            Environment.SetEnvironmentVariable("HARDCOVER_APIFY_GOODREADS_INPUT_TEMPLATE", null);
            Environment.SetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES", null);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("HARDCOVER_AUTH", _previousHardcoverAuth);
            Environment.SetEnvironmentVariable("HARDCOVER_API_KEY", _previousHardcoverApiKey);
            Environment.SetEnvironmentVariable("GOOGLE_BOOKS_API_KEY", _previousGoogleBooksKey);
            Environment.SetEnvironmentVariable("EUROPEANA_API_KEY", _previousEuropeanaKey);
            Environment.SetEnvironmentVariable("HARDCOVER_APIFY_GOODREADS_ACTOR", _previousApifyActor);
            Environment.SetEnvironmentVariable("HARDCOVER_APIFY_TOKEN", _previousApifyToken);
            Environment.SetEnvironmentVariable("HARDCOVER_APIFY_GOODREADS_INPUT_TEMPLATE", _previousApifyTemplate);
            Environment.SetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES", _previousSources);
        }

        [Test]
        public void should_never_return_saved_provider_credentials()
        {
            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(x => x.HardcoverAuth).Returns("hardcover-secret");
            config.SetupGet(x => x.GoogleBooksApiKey).Returns("google-secret");
            config.SetupGet(x => x.EuropeanaApiKey).Returns("europeana-secret");
            config.SetupGet(x => x.ApifyToken).Returns("apify-secret");
            config.SetupGet(x => x.ApifyGoodreadsActor).Returns("publisher~actor");
            config.SetupGet(x => x.ApifyGoodreadsInputTemplate).Returns("{\"queries\":[{{query}}]}");
            config.SetupGet(x => x.AdditionalMetadataSources).Returns("ndl");
            config.Setup(x => x.IsDefined("AdditionalMetadataSources")).Returns(true);

            var resource = MetadataProviderConfigResourceMapper.ToResource(config.Object);

            resource.HardcoverAuth.Should().BeEmpty();
            resource.GoogleBooksApiKey.Should().BeEmpty();
            resource.EuropeanaApiKey.Should().BeEmpty();
            resource.ApifyToken.Should().BeEmpty();
            resource.HasHardcoverAuth.Should().BeTrue();
            resource.HasGoogleBooksApiKey.Should().BeTrue();
            resource.HasEuropeanaApiKey.Should().BeTrue();
            resource.HasApifyToken.Should().BeTrue();
            resource.ApifyGoodreadsActor.Should().Be("publisher~actor");
            resource.EnableNdl.Should().BeTrue();
            resource.EnableLoc.Should().BeFalse();
        }

        [Test]
        public void should_preserve_new_settings_when_an_older_client_omits_them()
        {
            Dictionary<string, object> savedValues = null;
            var config = Mocker.GetMock<IConfigService>();
            config.Setup(x => x.SaveConfigDictionary(It.IsAny<Dictionary<string, object>>()))
                .Callback<Dictionary<string, object>>(values => savedValues = values);
            var controller = new MetadataProviderConfigController(config.Object);

            controller.SaveConfig(new MetadataProviderConfigResource { Id = 1 });

            savedValues.Should().NotContainKey("AdditionalMetadataSources");
            savedValues.Should().NotContainKey("HardcoverAuth");
            savedValues.Should().NotContainKey("GoogleBooksApiKey");
            savedValues.Should().NotContainKey("EuropeanaApiKey");
            savedValues.Should().NotContainKey("ApifyToken");
            savedValues.Should().NotContainKey("ApifyGoodreadsInputTemplate");
        }
    }
}
