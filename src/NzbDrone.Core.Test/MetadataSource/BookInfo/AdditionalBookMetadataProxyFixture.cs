using System;
using System.Net;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
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
                    var resource = request.Url.Host == "gutendex.com"
                        ? request.Url.Path.EndsWith("/books/", StringComparison.Ordinal) ? GutendexSearchResponse() : GutendexDetailResponse()
                        : request.Url.Host == "archive.org"
                            ? request.Url.Path.EndsWith("advancedsearch.php", StringComparison.Ordinal) ? InternetArchiveSearchResponse() : InternetArchiveDetailResponse()
                            : request.Url.Host == "www.loc.gov"
                                ? request.Url.Path.EndsWith("/books/", StringComparison.Ordinal) ? LocSearchResponse() : LocDetailResponse()
                                : request.Url.Path.EndsWith("search.json", StringComparison.Ordinal)
                                    ? SearchResponse()
                                    : DetailResponse();
                    return BookInfoTestData.TypedJsonResponse<JObject>(request, resource);
                });

            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get(It.Is<HttpRequest>(request => request.Url.Host == "ndlsearch.ndl.go.jp"), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) => new HttpResponse(
                    request,
                    new HttpHeader { { "Content-Type", "application/xml; charset=utf-8" } },
                    request.Url.Path.EndsWith("opensearch", StringComparison.Ordinal) ? NdlSearchResponse() : NdlDetailResponse(),
                    HttpStatusCode.OK));
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

        [Test]
        public void should_use_saved_ui_source_selection_and_credentials()
        {
            Environment.SetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES", null);
            Environment.SetEnvironmentVariable("EUROPEANA_API_KEY", null);
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.AdditionalMetadataSources)
                .Returns("europeana");
            Mocker.GetMock<IConfigService>()
                .SetupGet(x => x.EuropeanaApiKey)
                .Returns("saved-europeana-key");
            Mocker.GetMock<IConfigService>()
                .Setup(x => x.IsDefined("AdditionalMetadataSources"))
                .Returns(true);

            var books = CreateProxy().Search("Escrita criativa Rubens Marchioni");

            books.Should().ContainSingle();
            books[0].ForeignBookId.Should().StartWith("europeana:");
        }

        [Test]
        public void should_search_and_resolve_gutendex_books_and_authors_with_provider_identity()
        {
            Environment.SetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES", "gutendex");
            var proxy = CreateProxy();

            var books = proxy.Search("Alice's Adventures in Wonderland");

            books.Should().ContainSingle();
            var book = books[0];
            book.ForeignBookId.Should().Be("gutendex:11");
            book.AuthorMetadata.Value.ForeignAuthorId.Should().StartWith("gutendex-author:");
            book.Title.Should().Be("Alice's Adventures in Wonderland");
            book.Editions.Value[0].Language.Should().Be("en");

            var detail = proxy.GetBook(book.ForeignBookId);
            detail.Item1.Should().Be(book.AuthorMetadata.Value.ForeignAuthorId);
            detail.Item2.ForeignBookId.Should().Be(book.ForeignBookId);

            var author = proxy.GetAuthor(book.AuthorMetadata.Value.ForeignAuthorId);
            author.Name.Should().Be("Lewis Carroll");
            author.Books.Value.Should().ContainSingle().Which.ForeignBookId.Should().Be(book.ForeignBookId);
        }

        [Test]
        public void should_search_and_resolve_internet_archive_items_with_provider_identity()
        {
            Environment.SetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES", "internetarchive");
            var proxy = CreateProxy();

            var books = proxy.Search("Alice Adventures Wonderland");

            books.Should().ContainSingle();
            var book = books[0];
            book.ForeignBookId.Should().StartWith("internetarchive:");
            book.AuthorMetadata.Value.ForeignAuthorId.Should().StartWith("internetarchive-author:");
            book.Title.Should().Be("Alice's Adventures in Wonderland");
            book.Editions.Value[0].Isbn13.Should().Be("9781850812333");
            book.Editions.Value[0].Images.Should().ContainSingle();

            var detail = proxy.GetBook(book.ForeignBookId);
            detail.Item1.Should().Be(book.AuthorMetadata.Value.ForeignAuthorId);
            detail.Item2.ForeignBookId.Should().Be(book.ForeignBookId);

            var author = proxy.GetAuthor(book.AuthorMetadata.Value.ForeignAuthorId);
            author.Name.Should().Be("Carroll, Lewis, 1832-1898");
            author.Books.Value.Should().ContainSingle().Which.ForeignBookId.Should().Be(book.ForeignBookId);
        }

        [Test]
        public void should_search_and_resolve_ndl_records_with_provider_identity_without_artwork()
        {
            Environment.SetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES", "ndl");
            var proxy = CreateProxy();

            var books = proxy.Search("坊っちゃん");

            books.Should().ContainSingle();
            var book = books[0];
            book.ForeignBookId.Should().StartWith("ndl:");
            book.AuthorMetadata.Value.ForeignAuthorId.Should().StartWith("ndl-author:");
            book.Title.Should().Be("青い目の坊っちゃん");
            book.Editions.Value[0].Isbn13.Should().Be("9784575307061");
            book.Editions.Value[0].PageCount.Should().Be(251);
            book.Editions.Value[0].Images.Should().BeEmpty();

            var detail = proxy.GetBook(book.ForeignBookId);
            detail.Item1.Should().Be(book.AuthorMetadata.Value.ForeignAuthorId);
            detail.Item2.ForeignBookId.Should().Be(book.ForeignBookId);

            var author = proxy.GetAuthor(book.AuthorMetadata.Value.ForeignAuthorId);
            author.Name.Should().Be("ジョン・ストッカー");
            author.Books.Value.Should().ContainSingle().Which.ForeignBookId.Should().Be(book.ForeignBookId);
        }

        [Test]
        public void should_upgrade_historical_loc_http_ids_and_skip_invalid_search_rows()
        {
            Environment.SetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES", "loc");
            var proxy = CreateProxy();

            var books = proxy.Search("Alice's Adventures in Wonderland");

            books.Should().ContainSingle();
            books[0].ForeignBookId.Should().StartWith("loc:");
            books[0].Links.Should().ContainSingle().Which.Url.Should().StartWith("https://www.loc.gov/item/");

            var detail = proxy.GetBook(books[0].ForeignBookId);

            detail.Item2.ForeignBookId.Should().Be(books[0].ForeignBookId);
            Mocker.GetMock<ICachedHttpResponseService>().Verify(x => x.Get<JObject>(
                It.Is<HttpRequest>(request =>
                    request.Url.Scheme == "https" &&
                    request.Url.Host == "www.loc.gov" &&
                    request.Url.Path.EndsWith("/item/2017645977/", StringComparison.Ordinal)),
                true,
                It.IsAny<TimeSpan>()), Times.Once());
        }

        private AdditionalBookMetadataProxy CreateProxy() => new (
            Mocker.GetMock<IHttpClient>().Object,
            Mocker.GetMock<ICachedHttpResponseService>().Object,
            new CacheManager(),
            Mocker.GetMock<IConfigService>().Object,
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

        private static JObject GutendexSearchResponse() => new ()
        {
            ["results"] = new JArray(GutendexBook())
        };

        private static JObject GutendexDetailResponse() => GutendexBook();

        private static JObject GutendexBook() => new ()
        {
            ["id"] = 11,
            ["title"] = "Alice's Adventures in Wonderland",
            ["authors"] = new JArray(new JObject { ["name"] = "Lewis Carroll", ["birth_year"] = 1832, ["death_year"] = 1898 }),
            ["summaries"] = new JArray("A girl follows a white rabbit into a curious world."),
            ["languages"] = new JArray("en"),
            ["formats"] = new JObject { ["text/html"] = "https://www.gutenberg.org/ebooks/11.html.images" }
        };

        private static JObject InternetArchiveSearchResponse() => new ()
        {
            ["response"] = new JObject
            {
                ["docs"] = new JArray(InternetArchiveRecord())
            }
        };

        private static JObject InternetArchiveDetailResponse() => new ()
        {
            ["metadata"] = InternetArchiveRecord()
        };

        private static JObject InternetArchiveRecord() => new ()
        {
            ["identifier"] = "alicesadventures0000carr_y8t2",
            ["title"] = "Alice's Adventures in Wonderland",
            ["creator"] = new JArray("Carroll, Lewis, 1832-1898"),
            ["description"] = new JArray("A public catalog record."),
            ["date"] = "1994",
            ["language"] = "eng",
            ["isbn"] = new JArray("1850812330", "9781850812333")
        };

        private static JObject LocSearchResponse() => new ()
        {
            ["results"] = new JArray
            {
                new JObject { ["id"] = "https://catalog.example/item/not-a-loc-record/", ["title"] = new JArray("Invalid row") },
                LocRecord()
            }
        };

        private static JObject LocDetailResponse() => new ()
        {
            ["item"] = LocRecord()
        };

        private static JObject LocRecord() => new ()
        {
            ["id"] = "http://www.loc.gov/item/2017645977/",
            ["title"] = "Alice's Adventures in Wonderland",
            ["contributors"] = new JArray("Lewis Carroll"),
            ["publisher"] = new JArray("Macmillan"),
            ["language"] = new JArray("English"),
            ["date"] = "1865",
            ["description"] = new JArray("A girl follows a white rabbit."),
            ["subjects"] = new JArray("Fantasy")
        };

        private static string NdlSearchResponse() => @"
            <rss xmlns:dc=""http://purl.org/dc/elements/1.1/"" xmlns:dcndl=""http://ndl.go.jp/dcndl/terms/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
              <channel>
                <item>
                  <title>青い目の坊っちゃん</title>
                  <link>https://ndlsearch.ndl.go.jp/books/R100000001-I11141124078689</link>
                  <dc:creator>ジョン・ストッカー</dc:creator>
                  <dc:publisher>早川書房</dc:publisher>
                  <dc:date>1970</dc:date>
                  <dc:extent>251p ; 20cm</dc:extent>
                  <dc:identifier xsi:type=""dcndl:ISBN13"">9784575307061</dc:identifier>
                </item>
              </channel>
            </rss>";

        private static string NdlDetailResponse() => @"
            <searchRetrieveResponse xmlns=""http://www.loc.gov/zing/srw/"">
              <records><record><recordData>&lt;rdf:RDF xmlns:rdf=""http://www.w3.org/1999/02/22-rdf-syntax-ns#"" xmlns:dc=""http://purl.org/dc/elements/1.1/"" xmlns:dcterms=""http://purl.org/dc/terms/"" xmlns:dcndl=""http://ndl.go.jp/dcndl/terms/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""&gt;&lt;dcndl:BibResource&gt;&lt;dcterms:title&gt;青い目の坊っちゃん&lt;/dcterms:title&gt;&lt;dc:creator&gt;ジョン・ストッカー&lt;/dc:creator&gt;&lt;dc:publisher&gt;早川書房&lt;/dc:publisher&gt;&lt;dcterms:issued&gt;1970&lt;/dcterms:issued&gt;&lt;dcterms:extent&gt;251p ; 20cm&lt;/dcterms:extent&gt;&lt;dc:identifier xsi:type=""dcndl:ISBN13"">9784575307061&lt;/dc:identifier&gt;&lt;/dcndl:BibResource&gt;&lt;/rdf:RDF&gt;</recordData></record></records>
            </searchRetrieveResponse>";
    }
}
