using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using Readarr.Api.V1.Author;
using Readarr.Http;

namespace Readarr.Api.V1.Books
{
    [V1ApiController("book/lookup")]
    public class BookLookupController : Controller
    {
        private readonly ISearchForNewBook _searchProxy;
        private readonly IMapCoversToLocal _coverMapper;

        public BookLookupController(ISearchForNewBook searchProxy, IMapCoversToLocal coverMapper)
        {
            _searchProxy = searchProxy;
            _coverMapper = coverMapper;
        }

        [HttpGet]
        public object Search(string term)
        {
            var searchResults = _searchProxy.SearchForNewBook(term, null, interactiveSearch: true);
            return MapToResource(searchResults).ToList();
        }

        private IEnumerable<BookResource> MapToResource(IEnumerable<NzbDrone.Core.Books.Book> books)
        {
            foreach (var currentBook in books)
            {
                var resource = currentBook.ToResource();
                resource.Author = currentBook.Author.Value.ToResource();
                resource.Editions = currentBook.Editions.Value.ToResource();

                _coverMapper.ConvertToLocalUrls(resource.Id, MediaCoverEntity.Book, resource.Images);

                var cover = resource.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Cover);

                if (cover != null)
                {
                    resource.RemoteCover = cover.RemoteUrl;
                }

                yield return resource;
            }
        }
    }
}
