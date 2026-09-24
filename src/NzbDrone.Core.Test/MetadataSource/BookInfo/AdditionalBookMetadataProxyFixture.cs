using System;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    [TestFixture]
    public class AdditionalBookMetadataProxyFixture : CoreTest
    {
        private string _previousSources;
        private string _previousEuropeanaKey;

        [SetUp]
        public void Setup()
        {
            _previousSources = Environment.GetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES");
            _previousEuropeanaKey = Environment.GetEnvironmentVariable("EUROPEANA_API_KEY");
            Environment.SetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES", "europeana");
            Environment.SetEnvironmentVariable("EUROPEANA_API_KEY", "test-europeana-key");

            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get<JObject>(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) =>
                {
                    var resource = request.Url.Path.EndsWith("search.json", StringComparison.Ordinal)
                        ? SearchResponse()
                        : DetailResponse();
                    return BookInfoTestData.TypedJsonResponse<JObject>(request, resource);
                });
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES", _previousSources);
            Environment.SetEnvironmentVariable("EUROPEANA_API_KEY", _previousEuropeanaKey);
        }

        [Test]
        public void should_search_and_resolve_europeana_records_with_provider_identity()
        {
            var proxy = CreateProxy();

            var books = proxy.Search("Escrita criativa Rubens Marchioni");

            books.Should().ContainSingle();
            var book = books[0];
            book.ForeignBookId.Should().StartWith("europeana:");
            book.AuthorMetadata.Value.ForeignAuthorId.Should().StartWith("europeana-author:");
            book.Title.Should().Be("Escrita criativa da ideia ao texto");
            book.Editions.Value.Should().ContainSingle();
            book.Editions.Value[0].Language.Should().Be("por");
            book.Editions.Value[0].Isbn13.Should().Be("9783161484100");
            book.Editions.Value[0].Images.Should().ContainSingle();

            var detail = proxy.GetBook(book.ForeignBookId);
            detail.Item1.Should().Be(book.AuthorMetadata.Value.ForeignAuthorId);
            detail.Item2.ForeignBookId.Should().Be(book.ForeignBookId);
            detail.Item2.Title.Should().Be(book.Title);

            var author = proxy.GetAuthor(book.AuthorMetadata.Value.ForeignAuthorId);
            author.Name.Should().Be("Rubens Marchioni");
            author.Books.Value.Should().ContainSingle().Which.ForeignBookId.Should().Be(book.ForeignBookId);
        }

        private AdditionalBookMetadataProxy CreateProxy() => new (
            Mocker.GetMock<IHttpClient>().Object,
            Mocker.GetMock<ICachedHttpResponseService>().Object,
            new CacheManager(),
            TestLogger);

        private static JObject SearchResponse() => new ()
        {
            ["items"] = new JArray
            {
                new JObject
                {
                    ["id"] = "/202/record-1",
                    ["type"] = "TEXT",
                    ["title"] = new JArray("Escrita criativa da ideia ao texto"),
                    ["dcCreator"] = new JArray("Rubens Marchioni"),
                    ["dcIdentifier"] = new JArray("urn:isbn:978-3-16-148410-0"),
                    ["language"] = new JArray("por"),
                    ["edmPreview"] = new JArray("https://api.europeana.eu/thumbnail/record-1.jpg")
                }
            }
        };

        private static JObject DetailResponse() => new ()
        {
            ["object"] = new JObject
            {
                ["about"] = "/202/record-1",
                ["type"] = "TEXT",
                ["title"] = new JArray("Escrita criativa da ideia ao texto"),
                ["year"] = new JArray("2018"),
                ["language"] = new JArray("por"),
                ["proxies"] = new JArray
                {
                    new JObject
                    {
                        ["europeanaProxy"] = false,
                        ["dcCreator"] = new JArray("Rubens Marchioni"),
                        ["dcIdentifier"] = new JArray("urn:isbn:978-3-16-148410-0")
                    }
                },
                ["aggregations"] = new JArray
                {
                    new JObject { ["edmPreview"] = "https://api.europeana.eu/thumbnail/record-1.jpg" }
                }
            }
        };
    }
}
