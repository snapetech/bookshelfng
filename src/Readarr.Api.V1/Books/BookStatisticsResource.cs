using NzbDrone.Core.AuthorStats;

namespace Readarr.Api.V1.Books
{
    public class BookStatisticsResource
    {
        public int BookFileCount { get; set; }
        public int EbookFileCount { get; set; }
        public int AudiobookFileCount { get; set; }
        public int BookCount { get; set; }
        public int TotalBookCount { get; set; }
        public long SizeOnDisk { get; set; }
        public long EbookSizeOnDisk { get; set; }
        public long AudiobookSizeOnDisk { get; set; }

        public decimal PercentOfBooks
        {
            get
            {
                if (BookCount == 0)
                {
                    return 0;
                }

                return BookFileCount / (decimal)BookCount * 100;
            }
        }
    }

    public static class BookStatisticsResourceMapper
    {
        public static BookStatisticsResource ToResource(this BookStatistics model)
        {
            if (model == null)
            {
                return null;
            }

            return new BookStatisticsResource
            {
                BookFileCount = model.BookFileCount,
                EbookFileCount = model.EbookFileCount,
                AudiobookFileCount = model.AudiobookFileCount,
                BookCount = model.BookCount,
                SizeOnDisk = model.SizeOnDisk,
                EbookSizeOnDisk = model.EbookSizeOnDisk,
                AudiobookSizeOnDisk = model.AudiobookSizeOnDisk,
                TotalBookCount = model.TotalBookCount
            };
        }
    }
}
