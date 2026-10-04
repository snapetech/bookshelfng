using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AuthorStats;
using Readarr.Api.V1.Books;

namespace NzbDrone.Api.Test.Books
{
    [TestFixture]
    public class BookStatisticsResourceFixture
    {
        [Test]
        public void should_map_format_specific_file_counts_and_sizes()
        {
            var resource = new BookStatistics
            {
                BookFileCount = 3,
                EbookFileCount = 1,
                AudiobookFileCount = 2,
                SizeOnDisk = 450,
                EbookSizeOnDisk = 100,
                AudiobookSizeOnDisk = 350
            }.ToResource();

            resource.BookFileCount.Should().Be(3);
            resource.EbookFileCount.Should().Be(1);
            resource.AudiobookFileCount.Should().Be(2);
            resource.SizeOnDisk.Should().Be(450);
            resource.EbookSizeOnDisk.Should().Be(100);
            resource.AudiobookSizeOnDisk.Should().Be(350);
        }
    }
}
