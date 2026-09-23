using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Test.MetadataSource.BookInfo;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.Goodreads
{
    [TestFixture]
    public class BookInfoProxySearchFixture : CoreTest<BookInfoProxy>
    {
        [SetUp]
        public void Setup()
        {
            UseRealHttp();

            Mocker.GetMock<IGoodreadsSearchProxy>()
                .Setup(x => x.Search(It.IsAny<string>()))
                .Returns((string query) => BookInfoTestData.BookInfoSearchResults(query));

            Mocker.GetMock<ICachedHttpResponseService>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<TimeSpan>()))
                .Returns((HttpRequest request, bool useCache, TimeSpan ttl) => BookInfoTestData.DetailResponse(request));

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    request.Url.Path.Trim('/').StartsWith("book/", StringComparison.Ordinal)
                        ? BookInfoTestData.EditionLookupResponse(request)
                        : BookInfoTestData.DetailResponse(request));

            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Post<BulkBookResource>(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                    BookInfoTestData.TypedJsonResponse<BulkBookResource>(request, BookInfoTestData.CombinedSearchResponse()));

            var metadataProfile = new MetadataProfile();

            Mocker.GetMock<IMetadataProfileService>()
                .Setup(s => s.All())
                .Returns(new List<MetadataProfile> { metadataProfile });

            Mocker.GetMock<IMetadataProfileService>()
                .Setup(s => s.Get(It.IsAny<int>()))
                .Returns(metadataProfile);

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.MetadataSource)
                .Returns("https://api.bookinfo.pro");
        }

        [TestCase("Robert Harris", "Robert Harris")]
        [TestCase("Lyndsay Ely", "Lyndsay Ely")]
        [TestCase("Elisa Puricelli Guerra", "Elisa Puricelli Guerra")]
        public void successful_author_search(string title, string expected)
        {
            var result = Subject.SearchForNewAuthor(title);

            result.Should().NotBeEmpty();

            result[0].Name.Should().Be(expected);

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void should_include_additional_catalog_authors_in_author_lookup()
        {
            var authorMetadata = new AuthorMetadata
            {
                ForeignAuthorId = "googlebooks-author:cnViZW5zIG1hcmNoaW9uaQ",
                Name = "Rubens Marchioni",
            };
            var catalogBook = new Book
            {
                ForeignBookId = "googlebooks:volume-rubens",
                Title = "Escrita criativa da ideia ao texto",
                AuthorMetadata = authorMetadata,
                Author = new Author
                {
                    CleanName = "Rubens Marchioni",
                    Metadata = authorMetadata,
                },
            };

            Mocker.GetMock<IGoodreadsSearchProxy>()
                .Setup(x => x.Search(It.IsAny<string>()))
                .Returns(new List<SearchJsonResource>());
            Mocker.GetMock<IAdditionalBookMetadataProxy>()
                .Setup(x => x.Search(It.IsAny<string>()))
                .Returns(new List<Book> { catalogBook });

            var authors = Subject.SearchForNewAuthor("Rubens Marchioni");

            authors.Should().ContainSingle();
            authors[0].Name.Should().Be("Rubens Marchioni");
            authors[0].ForeignAuthorId.Should().StartWith("googlebooks-author:");
        }

        //[TestCase("asin:B0192CTMYG", null, "Harry Potter and the Sorcerer's Stone")] // ASIN not working
        [TestCase("Harry Potter and the sorcerer's stone a summary of the novel", null, "Harry Potter and the Sorcerer's Stone (Book 1)")]
        [TestCase("edition:3", null, "Harry Potter and the Sorcerer's Stone")]
        [TestCase("edition: 3", null, "Harry Potter and the Sorcerer's Stone")]
        [TestCase("isbn:9780439554930", null, "Harry Potter and the Sorcerer's Stone")]
        public void successful_book_search(string title, string author, string expected)
        {
            var result = Subject.SearchForNewBook(title, author, false);

            result.Should().NotBeEmpty();

            result[0].Editions.Value[0].Title.Should().Be(expected);

            ExceptionVerification.IgnoreWarns();
            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void should_resolve_provider_qualified_book_ids_without_goodreads_numeric_parsing()
        {
            var book = new Book
            {
                ForeignBookId = "googlebooks:volume-123",
                Title = "Provider Book",
            };
            Mocker.GetMock<IAdditionalBookMetadataProxy>()
                .Setup(x => x.HandlesBookId("googlebooks:volume-123"))
                .Returns(true);
            Mocker.GetMock<IAdditionalBookMetadataProxy>()
                .Setup(x => x.GetBook("googlebooks:volume-123"))
                .Returns(Tuple.Create("googlebooks:volume-123", book, new List<AuthorMetadata>()));

            var result = Subject.SearchForNewBook("googlebooks:volume-123", null);

            result.Should().ContainSingle().Which.ForeignBookId.Should().Be("googlebooks:volume-123");
            Mocker.GetMock<IGoodreadsSearchProxy>()
                .Verify(x => x.Search(It.IsAny<string>()), Times.Never);
        }

        [TestCase("edition:")]
        [TestCase("edition: 99999999999999999999")]
        [TestCase("edition: 0")]
        [TestCase("edition: -12")]
        [TestCase("edition: aaaa")]
        [TestCase("adjalkwdjkalwdjklawjdlKAJD")]
        public void no_author_search_result(string term)
        {
            var result = Subject.SearchForNewAuthor(term);
            result.Should().BeEmpty();

            ExceptionVerification.IgnoreWarns();
        }

        [TestCase("Roald Dahl", 0, typeof(Author), new[] { "Roald Dahl" }, TestName = "author")]
        [TestCase("Roald Dahl", 1, typeof(Book), new[] { "Matilda" }, TestName = "book")]
        public void successful_combined_search(string query, int position, Type resultType, string[] expected)
        {
            var result = Subject.SearchForNewEntity(query);
            result.Should().NotBeEmpty();
            result[position].GetType().Should().Be(resultType);

            if (resultType == typeof(Author))
            {
                var cast = result[position] as Author;
                cast.Should().NotBeNull();
                cast.Name.Should().ContainAny(expected);
            }
            else
            {
                var cast = result[position] as Book;
                cast.Should().NotBeNull();
                cast.Title.Should().ContainAny(expected);
            }
        }
    }
}
