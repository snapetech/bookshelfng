using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class CandidateServiceFixture : CoreTest<CandidateService>
    {
        [Test]
        public void should_not_throw_on_goodreads_exception()
        {
            Mocker.GetMock<ISearchForNewBook>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Throws(new GoodreadsException("Bad search"));

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "Author" },
                            BookTitle = "Book"
                        }
                    }
                }
            };

            Subject.GetRemoteCandidates(edition, null).Should().BeEmpty();
        }

        [Test]
        public void should_search_by_title_when_filename_has_no_author()
        {
            Mocker.GetMock<ISearchForNewBook>()
                .Setup(s => s.SearchForNewBook("Rogue Elements", null, true))
                .Returns(new List<Book>());

            var edition = new LocalEdition
            {
                LocalBooks = new List<LocalBook>
                {
                    new LocalBook
                    {
                        Path = "/incoming/Rogue Elements.epub",
                        FileTrackInfo = new ParsedTrackInfo
                        {
                            Authors = new List<string> { "Rogue" },
                            BookTitle = "Elements"
                        }
                    }
                }
            };

            Mocker.GetMock<ISearchForNewBook>()
                .Setup(s => s.SearchForNewBook(It.IsAny<string>(), It.IsAny<string>(), true))
                .Returns(new List<Book>());

            Subject.GetRemoteCandidates(edition, null).Should().BeEmpty();
            Mocker.GetMock<ISearchForNewBook>()
                .Verify(s => s.SearchForNewBook("Rogue Elements", null, true), Times.Once);
        }
    }
}
