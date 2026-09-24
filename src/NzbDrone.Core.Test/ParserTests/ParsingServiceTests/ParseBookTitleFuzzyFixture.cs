using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    [TestFixture]
    public class ParseBookTitleFuzzyFixture : CoreTest<ParsingService>
    {
        private const string ReleaseTitle = "Jane Austen - The Hobbit";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(config => config.BookImportMinimumMatchPercent)
                .Returns(70);
        }

        [Test]
        public void should_keep_the_best_book_title_match()
        {
            var matchingAuthor = new Author { AuthorMetadataId = 1, Name = "Jane Austen" };
            var unrelatedAuthor = new Author { AuthorMetadataId = 2, Name = "Unknown Writer" };
            var matchingBook = CreateBook(1, "The Hobbit", "The Hobbit");
            var weakerBook = CreateBook(2, "The Hobbiq", "The Hobbiq");

            GivenAuthors(matchingAuthor, unrelatedAuthor);
            GivenBooks(1, matchingBook);
            GivenBooks(2, weakerBook);

            var result = Subject.ParseBookTitleFuzzy(ReleaseTitle);

            result.Should().NotBeNull();
            result.AuthorName.Should().Be("Jane Austen");
            result.BookTitle.Should().Be("The Hobbit");
        }

        [Test]
        public void should_keep_the_best_edition_title_match()
        {
            var matchingAuthor = new Author { AuthorMetadataId = 1, Name = "Jane Austen" };
            var unrelatedAuthor = new Author { AuthorMetadataId = 2, Name = "Unknown Writer" };
            var matchingBook = CreateBook(1, "Unknown Catalog Title", "The Hobbit");
            var weakerBook = CreateBook(2, "The Hobbiq", "The Hobbiq");
            var matchingEdition = matchingBook.Editions.Value[0];

            GivenAuthors(matchingAuthor, unrelatedAuthor);
            GivenBooks(1);
            GivenBooks(2, weakerBook);
            GivenEditions(1, matchingEdition);

            var result = Subject.ParseBookTitleFuzzy(ReleaseTitle);

            result.Should().NotBeNull();
            result.AuthorName.Should().Be("Jane Austen");
            result.BookTitle.Should().Be("The Hobbit");
        }

        [Test]
        public void should_reject_book_title_matches_below_the_configured_minimum()
        {
            var author = new Author { AuthorMetadataId = 1, Name = "Jane Austen" };
            var weakCatalogMatch = CreateBook(1, "The Zzzzzzzzzz", "The Hobbit");

            GivenAuthors(author);
            GivenBooks(1, weakCatalogMatch);

            Subject.ParseBookTitleFuzzy(ReleaseTitle).Should().BeNull();
        }

        private void GivenAuthors(params Author[] authors)
        {
            Mocker.GetMock<IAuthorService>()
                .Setup(service => service.GetReportCandidates(ReleaseTitle))
                .Returns(new List<Author>(authors));
        }

        private void GivenBooks(int authorId, params Book[] books)
        {
            Mocker.GetMock<IBookService>()
                .Setup(service => service.GetCandidates(authorId, ReleaseTitle))
                .Returns(new List<Book>(books));

            Mocker.GetMock<IEditionService>()
                .Setup(service => service.GetCandidates(authorId, ReleaseTitle))
                .Returns(new List<Edition>());
        }

        private void GivenEditions(int authorId, params Edition[] editions)
        {
            Mocker.GetMock<IEditionService>()
                .Setup(service => service.GetCandidates(authorId, ReleaseTitle))
                .Returns(new List<Edition>(editions));
        }

        private static Book CreateBook(int authorId, string title, string editionTitle)
        {
            var book = new Book
            {
                AuthorMetadataId = authorId,
                Title = title
            };

            var edition = new Edition
            {
                Book = book,
                Title = editionTitle,
                Monitored = true
            };

            book.Editions = new List<Edition> { edition };

            return book;
        }
    }
}
