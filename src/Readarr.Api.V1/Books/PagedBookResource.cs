using System.Collections.Generic;

namespace Readarr.Api.V1.Books
{
    public class PagedBookResource
    {
        public List<BookResource> Records { get; set; }
        public int Offset { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
    }
}
