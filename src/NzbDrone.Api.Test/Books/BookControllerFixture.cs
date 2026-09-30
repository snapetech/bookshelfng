using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Books;

namespace NzbDrone.Api.Test.Books
{
    [TestFixture]
    public class BookControllerFixture : TestBase<BookController>
    {
        [Test]
        public void should_query_a_bounded_page_and_report_the_full_library_count()
        {
            PagingSpec<Book> received = null;
            Mocker.GetMock<IBookService>()
                .Setup(service => service.GetPagedBooks(It.IsAny<PagingSpec<Book>>()))
                .Returns((PagingSpec<Book> pagingSpec) =>
                {
                    received = pagingSpec;
                    pagingSpec.TotalRecords = 321;
                    pagingSpec.Records = new List<Book>();
                    return pagingSpec;
                });
            Mocker.GetMock<ISeriesBookLinkService>()
                .Setup(service => service.GetLinksByBook(It.IsAny<List<int>>()))
                .Returns(new List<SeriesBookLink>());

            var result = Subject.GetPagedBooks(150, 100);

            received.Should().NotBeNull();
            received.Page.Should().Be(2);
            received.PageSize.Should().Be(150);
            received.SortKey.Should().Be("Title");
            received.SortDirection.Should().Be(SortDirection.Ascending);
            result.Offset.Should().Be(150);
            result.PageSize.Should().Be(100);
            result.TotalCount.Should().Be(321);
            result.Records.Should().BeEmpty();
        }

        [Test]
        public void should_cap_requested_page_size()
        {
            PagingSpec<Book> received = null;
            Mocker.GetMock<IBookService>()
                .Setup(service => service.GetPagedBooks(It.IsAny<PagingSpec<Book>>()))
                .Returns((PagingSpec<Book> pagingSpec) =>
                {
                    received = pagingSpec;
                    pagingSpec.TotalRecords = 0;
                    pagingSpec.Records = new List<Book>();
                    return pagingSpec;
                });
            Mocker.GetMock<ISeriesBookLinkService>()
                .Setup(service => service.GetLinksByBook(It.IsAny<List<int>>()))
                .Returns(new List<SeriesBookLink>());

            var result = Subject.GetPagedBooks(0, 10000);

            received.PageSize.Should().Be(200);
            result.PageSize.Should().Be(200);
        }
    }
}
