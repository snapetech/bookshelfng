using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Books;

namespace NzbDrone.Api.Test.Books
{
    [TestFixture]
    public class BookLookupControllerFixture : TestBase<BookLookupController>
    {
        [Test]
        public void should_include_author_and_editions_in_lookup_results()
        {
            var author = new Author
            {
                Metadata = new AuthorMetadata
                {
                    ForeignAuthorId = "656983",
                    Name = "J.R.R. Tolkien",
                    NameLastFirst = "Tolkien, J.R.R.",
                    SortName = "j r r tolkien",
                    SortNameLastFirst = "tolkien j r r"
                }
            };

            var edition = new Edition
            {
                ForeignEditionId = "5907",
                Isbn13 = "9780547928227",
                Title = "The Hobbit",
                Monitored = true
            };

            var book = new Book
            {
                ForeignBookId = "1540236",
                Title = "The Hobbit",
                Author = author,
                AuthorMetadata = author.Metadata.Value,
                Editions = new List<Edition> { edition },
                SeriesLinks = new List<SeriesBookLink>()
            };

            Mocker.GetMock<ISearchForNewBook>()
                .Setup(s => s.SearchForNewBook("hobbit", null, true, true))
                .Returns(new List<Book> { book });

            var result = ((IEnumerable<BookResource>)Subject.Search("hobbit")).ToList();

            result.Should().HaveCount(1);
            result[0].Author.Should().NotBeNull();
            result[0].Author.ForeignAuthorId.Should().Be("656983");
            result[0].Editions.Should().HaveCount(1);
            result[0].Editions[0].ForeignEditionId.Should().Be("5907");
            result[0].Editions[0].Monitored.Should().BeTrue();

            Mocker.GetMock<IMapCoversToLocal>()
                .Verify(v => v.ConvertToLocalUrls(0, MediaCoverEntity.Book, It.IsAny<IEnumerable<MediaCover>>()), Times.Once);
        }
    }
}
