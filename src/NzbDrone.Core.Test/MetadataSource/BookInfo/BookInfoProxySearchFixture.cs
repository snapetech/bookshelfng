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
