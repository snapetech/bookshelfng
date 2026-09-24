using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using LazyCache;
using LazyCache.Providers;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Http;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.Goodreads;
using NzbDrone.Core.MetadataSource.Hardcover;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    public class BookInfoProxy : IProvideAuthorInfo, IProvideBookInfo, ISearchForNewBook, ISearchForNewAuthor, ISearchForNewEntity
    {
        private static readonly JsonSerializerOptions SerializerSettings = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            Converters = { new STJUtcConverter() }
        };

        private readonly IHttpClient _httpClient;
        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly IGoodreadsSearchProxy _goodreadsSearchProxy;
        private readonly IAuthorService _authorService;
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly Logger _logger;
        private readonly IMetadataRequestBuilder _requestBuilder;
        private readonly IHardcoverMetadataProxy _hardcoverMetadataProxy;
        private readonly IAdditionalBookMetadataProxy _additionalBookMetadataProxy;
        private readonly ICached<HashSet<string>> _cache;
        private readonly CachingService _authorCache;

        public BookInfoProxy(IHttpClient httpClient,
                             ICachedHttpResponseService cachedHttpClient,
                             IGoodreadsSearchProxy goodreadsSearchProxy,
                             IAuthorService authorService,
                             IBookService bookService,
                             IEditionService editionService,
                             IMetadataRequestBuilder requestBuilder,
                             IHardcoverMetadataProxy hardcoverMetadataProxy,
                             IAdditionalBookMetadataProxy additionalBookMetadataProxy,
                             Logger logger,
                             ICacheManager cacheManager)
        {
            _httpClient = httpClient;
            _cachedHttpClient = cachedHttpClient;
            _goodreadsSearchProxy = goodreadsSearchProxy;
            _authorService = authorService;
            _bookService = bookService;
            _editionService = editionService;
            _requestBuilder = requestBuilder;
            _hardcoverMetadataProxy = hardcoverMetadataProxy;
            _additionalBookMetadataProxy = additionalBookMetadataProxy;
            _cache = cacheManager.GetCache<HashSet<string>>(GetType());
            _logger = logger;

            _authorCache = new CachingService(new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 })));
            _authorCache.DefaultCachePolicy = new CacheDefaults
            {
                DefaultCacheDurationSeconds = 60
            };
        }

        public HashSet<string> GetChangedAuthors(DateTime startTime)
        {
            if (_hardcoverMetadataProxy.IsNativeEnabled)
            {
                // Hardcover does not expose the legacy Readarr changed-author
                // feed. Scheduled refreshes continue to use the local queue.
                return null;
            }

            var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                .SetSegment("route", "author/changed")
                .AddQueryParam("since", startTime.ToString("o"))
                .Build();

            httpRequest.SuppressHttpError = true;

            var httpResponse = _httpClient.Get<RecentUpdatesResource>(httpRequest);

            if (httpResponse.Resource == null || httpResponse.Resource.Limited)
            {
                return null;
            }

            return new HashSet<string>(httpResponse.Resource.Ids.Select(x => x.ToString()));
        }

        public Author GetAuthorInfo(string foreignAuthorId, bool useCache = false)
        {
            return GetAuthorInfo(foreignAuthorId, useCache, false);
        }

        private Author GetAuthorInfo(string foreignAuthorId, bool useCache, bool interactiveSearch)
        {
            if (_additionalBookMetadataProxy.HandlesAuthorId(foreignAuthorId))
            {
                return _additionalBookMetadataProxy.GetAuthor(foreignAuthorId);
            }

            if (TryGetNamespacedId(foreignAuthorId, "metadata-api-author:", out var metadataApiAuthorId))
            {
                return NamespaceMetadataApiAuthor(PollAuthorUncached(metadataApiAuthorId));
            }

            if (TryGetNamespacedId(foreignAuthorId, "hardcover-author:", out var hardcoverAuthorId))
            {
                return NamespaceHardcoverAuthor(MapAuthor(_hardcoverMetadataProxy.GetAuthor(hardcoverAuthorId, interactiveSearch)));
            }

            if (_hardcoverMetadataProxy.IsNativeEnabled)
            {
                return MapAuthor(_hardcoverMetadataProxy.GetAuthor(foreignAuthorId, interactiveSearch));
            }

            _logger.Debug("Getting Author details GoodreadsId of {0}", foreignAuthorId?.ReplaceLineEndings(""));

            try
            {
                if (useCache)
                {
                    return PollAuthor(foreignAuthorId);
                }

                return PollAuthorUncached(foreignAuthorId);
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Unexpected error getting author info: {foreignAuthorId}", foreignAuthorId?.ReplaceLineEndings(""));
                throw;
            }
        }

        public HashSet<string> GetChangedBooks(DateTime startTime)
        {
            return _cache.Get("ChangedBooks", () => GetChangedBooksUncached(startTime), TimeSpan.FromMinutes(30));
        }

        private HashSet<string> GetChangedBooksUncached(DateTime startTime)
        {
            return null;
        }

        public Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string foreignBookId)
        {
            return GetBookInfo(foreignBookId, false);
        }

        private Tuple<string, Book, List<AuthorMetadata>> GetBookInfo(string foreignBookId, bool interactiveSearch)
        {
            if (_additionalBookMetadataProxy.HandlesBookId(foreignBookId))
            {
                return ApplyFieldSourcePreferences(_additionalBookMetadataProxy.GetBook(foreignBookId));
            }

            if (TryGetNamespacedId(foreignBookId, "metadata-api:", out var metadataApiBookId))
            {
                return ApplyFieldSourcePreferences(NamespaceMetadataApiBook(PollBook(metadataApiBookId)));
            }

            if (TryGetNamespacedId(foreignBookId, "hardcover:", out var hardcoverBookId))
            {
                return ApplyFieldSourcePreferences(GetHardcoverBook(hardcoverBookId, true, interactiveSearch));
            }

            if (_hardcoverMetadataProxy.IsNativeEnabled)
            {
                var resource = _hardcoverMetadataProxy.GetWork(foreignBookId, interactiveSearch);
                var book = MapBook(resource);
                var authorId = GetAuthorId(resource).ToString();
                var metadata = resource.Authors.Select(MapAuthorMetadata).ToList();

                MapSeriesLinks(resource.Series.Select(MapSeries).ToList(), new List<Book> { book }, resource.Series);

                return ApplyFieldSourcePreferences(Tuple.Create(authorId, book, metadata));
            }

            try
            {
                return ApplyFieldSourcePreferences(PollBook(foreignBookId));
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Unexpected error getting book info: {foreignBookId}", foreignBookId?.ReplaceLineEndings(""));
                throw;
            }
        }

        private Tuple<string, Book, List<AuthorMetadata>> ApplyFieldSourcePreferences(Tuple<string, Book, List<AuthorMetadata>> bookInfo)
        {
            if (bookInfo?.Item2 != null)
            {
                _additionalBookMetadataProxy.ApplyFieldSourcePreferences(bookInfo.Item2);
            }

            return bookInfo;
        }

        public List<object> SearchForNewEntity(string title)
        {
            var books = SearchForNewBook(title, null, false, true);

            var result = new List<object>();
            foreach (var book in books)
            {
                var author = book.Author.Value;

                if (!result.Contains(author))
                {
                    result.Add(author);
                }

                result.Add(book);
            }

            return result;
        }

        public List<Author> SearchForNewAuthor(string title)
        {
            var books = SearchForNewBook(title, null, true, true);

            return books
                .Select(x => x.Author.Value)
                .DistinctBy(x => x.ForeignAuthorId)
                .ToList();
        }

        public List<Book> SearchForNewBook(string title, string author, bool getAllEditions = true, bool interactiveSearch = false)
        {
            if (_additionalBookMetadataProxy.HandlesBookId(title))
            {
                return new List<Book> { _additionalBookMetadataProxy.GetBook(title).Item2 };
            }

            if (TryGetNamespacedId(title, "metadata-api:", out _) ||
                TryGetNamespacedId(title, "hardcover:", out _))
            {
                return new List<Book> { GetBookInfo(title, interactiveSearch).Item2 };
            }

            if (_additionalBookMetadataProxy.HandlesAuthorId(title))
            {
                return _additionalBookMetadataProxy.GetAuthor(title).Books.Value;
            }

            if (TryGetNamespacedId(title, "metadata-api-author:", out _) ||
                TryGetNamespacedId(title, "hardcover-author:", out _))
            {
                return GetAuthorInfo(title, false, interactiveSearch).Books.Value;
            }

            var q = title.ToLower().Trim();
            if (author != null)
            {
                q += " " + author;
            }

            try
            {
                var lowerTitle = title.ToLowerInvariant();

                var split = lowerTitle.Split(':');
                var prefix = split[0];

                if (split.Length == 2 && new[] { "author", "work", "edition", "isbn", "asin" }.Contains(prefix))
                {
                    var slug = split[1].Trim();

                    if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace))
                    {
                        return new List<Book>();
                    }

                    if (prefix == "author" || prefix == "work" || prefix == "edition")
                    {
                        var isValid = int.TryParse(slug, out var searchId);
                        if (!isValid)
                        {
                            return new List<Book>();
                        }

                        if (prefix == "author")
                        {
                            return SearchByGoodreadsAuthorId(searchId);
                        }

                        if (prefix == "work")
                        {
                            return SearchByGoodreadsWorkId(searchId);
                        }

                        if (prefix == "edition")
                        {
                            return SearchByGoodreadsBookId(searchId, getAllEditions);
                        }
                    }

                    // to handle isbn / asin
                    q = slug;
                }

                return SearchWithAdditional(q, getAllEditions, interactiveSearch);
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex, ex.Message);
                throw new GoodreadsException("Search for '{0}' failed. Unable to communicate with Goodreads.", ex, title);
            }
            catch (Exception ex) when (ex is not BookInfoException)
            {
                _logger.Warn(ex, ex.Message);
                throw new GoodreadsException("Search for '{0}' failed. Invalid response received from Goodreads.", ex, title);
            }
        }

        public List<Book> SearchByIsbn(string isbn)
        {
            return SearchWithAdditional(isbn, true);
        }

        public List<Book> SearchByAsin(string asin)
        {
            return SearchWithAdditional(asin, true);
        }

        private List<Book> SearchWithAdditional(string query, bool getAllEditions, bool interactiveSearch = false)
        {
            var defaultPrimarySource = _hardcoverMetadataProxy.IsNativeEnabled
                ? AdditionalMetadataSources.Hardcover
                : AdditionalMetadataSources.MetadataApi;
            var sources = _additionalBookMetadataProxy.GetEnabledCatalogSources(
                defaultPrimarySource,
                _hardcoverMetadataProxy.IsConfigured);

            // Keep compatibility with old mocks and proxy implementations that
            // predate runtime catalog selection.
            if (sources == null)
            {
                var legacyBooks = Search(query, getAllEditions);
                try
                {
                    legacyBooks.AddRange(_additionalBookMetadataProxy.Search(query));
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Additional book metadata search failed for {0}", query?.ReplaceLineEndings(""));
                }

                return legacyBooks.DistinctBy(x => x.ForeignBookId).ToList();
            }

            var books = new List<Book>();
            foreach (var provider in sources
                .OrderBy(x => GetCatalogSearchPriority(x, defaultPrimarySource))
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (provider == AdditionalMetadataSources.Hardcover)
                    {
                        books.AddRange(SearchHardcoverCatalog(query, provider != defaultPrimarySource, interactiveSearch));
                    }
                    else if (provider == AdditionalMetadataSources.MetadataApi)
                    {
                        books.AddRange(SearchMetadataApiCatalog(query, getAllEditions, provider != defaultPrimarySource));
                    }
                    else
                    {
                        books.AddRange(_additionalBookMetadataProxy.SearchProvider(query, provider));
                    }
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Metadata catalog provider {0} failed for {1}", provider, query?.ReplaceLineEndings(""));
                }
            }

            return books.Where(x => !x.ForeignBookId.IsNullOrWhiteSpace())
                .DistinctBy(x => x.ForeignBookId)
                .ToList();
        }

        private static int GetCatalogSearchPriority(string provider, string defaultPrimarySource)
        {
            if (string.Equals(provider, defaultPrimarySource, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (provider == AdditionalMetadataSources.Hardcover || provider == AdditionalMetadataSources.MetadataApi)
            {
                return 1;
            }

            return provider switch
            {
                AdditionalMetadataSources.OpenLibrary => 2,
                "googlebooks" => 3,
                "gutendex" => 4,
                "internetarchive" => 5,
                "europeana" => 6,
                "loc" => 7,
                "ndl" => 8,
                "apify-goodreads" => 9,
                _ => 10
            };
        }

        private List<Book> SearchMetadataApiCatalog(string query, bool getAllEditions, bool namespaceIds)
        {
            var results = _goodreadsSearchProxy.SearchMetadataApi(query) ?? new List<SearchJsonResource>();
            if (results.Count == 0)
            {
                return new List<Book>();
            }

            if (getAllEditions)
            {
                var books = new List<Book>();
                foreach (var authorResults in results
                    .Where(x => x.Author != null && x.Author.Id > 0 && x.WorkId > 0)
                    .GroupBy(x => x.Author.Id))
                {
                    try
                    {
                        var authorId = authorResults.Key.ToString();
                        var requestedWorkIds = authorResults.Select(x => x.WorkId.ToString()).ToHashSet(StringComparer.Ordinal);
                        var author = PollAuthorUncached(authorId);
                        var authors = new Dictionary<string, AuthorMetadata>
                        {
                            [authorId] = author.Metadata.Value
                        };

                        foreach (var book in author.Books.Value.Where(x => requestedWorkIds.Contains(x.ForeignBookId)))
                        {
                            if (!namespaceIds)
                            {
                                AddDbIds(authorId, book, authors);
                            }

                            var bookInfo = Tuple.Create(
                                authorId,
                                book,
                                new List<AuthorMetadata> { book.AuthorMetadata.Value });
                            books.Add(namespaceIds ? NamespaceMetadataApiBook(bookInfo).Item2 : book);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.Warn(e, "Readarr-compatible metadata catalog author {0} failed", authorResults.Key);
                    }
                }

                return books;
            }

            var editionIds = results.Select(x => x.BookId).Where(x => x > 0).Distinct().ToList();
            if (editionIds.Count == 0)
            {
                return new List<Book>();
            }

            if (editionIds.Count == 1)
            {
                var book = GetEditionInfo(editionIds[0], false, false, !namespaceIds);
                return new List<Book>
                {
                    namespaceIds
                        ? NamespaceMetadataApiBook(Tuple.Create(
                            book.AuthorMetadata.Value.ForeignAuthorId,
                            book,
                            new List<AuthorMetadata> { book.AuthorMetadata.Value })).Item2
                        : book
                };
            }

            return MapSearchResult(editionIds, !namespaceIds)
                .Select(book => namespaceIds
                    ? NamespaceMetadataApiBook(Tuple.Create(
                        book.AuthorMetadata.Value.ForeignAuthorId,
                        book,
                        new List<AuthorMetadata> { book.AuthorMetadata.Value })).Item2
                    : book)
                .ToList();
        }

        private List<Book> SearchHardcoverCatalog(string query, bool namespaceIds, bool interactiveSearch)
        {
            var books = new List<Book>();
            var results = _hardcoverMetadataProxy.Search(query, interactiveSearch) ?? new List<SearchJsonResource>();
            foreach (var result in results.Where(x => x.WorkId > 0).DistinctBy(x => x.WorkId))
            {
                try
                {
                    books.Add(GetHardcoverBook(result.WorkId.ToString(), namespaceIds, interactiveSearch).Item2);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Hardcover catalog work {0} failed", result.WorkId);
                }
            }

            return books;
        }

        private Tuple<string, Book, List<AuthorMetadata>> GetHardcoverBook(string workId, bool namespaceIds = true, bool interactiveSearch = false)
        {
            var resource = _hardcoverMetadataProxy.GetWork(workId, interactiveSearch);
            var book = MapBook(resource);
            var authors = resource.Authors.Select(MapAuthorMetadata).ToList();
            var authorId = GetAuthorId(resource).ToString();
            var author = authors.FirstOrDefault(x => x.ForeignAuthorId == authorId) ?? authors.FirstOrDefault();

            if (author == null)
            {
                throw new AuthorNotFoundException(authorId);
            }

            book.AuthorMetadata = author;
            book.Author.Value.Metadata = author;
            MapSeriesLinks(resource.Series.Select(MapSeries).ToList(), new List<Book> { book }, resource.Series);

            var bookInfo = Tuple.Create(authorId, book, authors);
            if (!namespaceIds)
            {
                AddDbIds(authorId, book, authors.ToDictionary(x => x.ForeignAuthorId));
                return bookInfo;
            }

            return NamespaceHardcoverBook(bookInfo);
        }

        private static Tuple<string, Book, List<AuthorMetadata>> NamespaceMetadataApiBook(
            Tuple<string, Book, List<AuthorMetadata>> bookInfo)
        {
            return NamespaceBookInfo(bookInfo, "metadata-api:", "metadata-api-author:");
        }

        private static Tuple<string, Book, List<AuthorMetadata>> NamespaceHardcoverBook(
            Tuple<string, Book, List<AuthorMetadata>> bookInfo)
        {
            return NamespaceBookInfo(bookInfo, "hardcover:", "hardcover-author:");
        }

        private static Tuple<string, Book, List<AuthorMetadata>> NamespaceBookInfo(
            Tuple<string, Book, List<AuthorMetadata>> bookInfo,
            string bookPrefix,
            string authorPrefix)
        {
            var book = bookInfo.Item2;
            var authors = bookInfo.Item3 ?? new List<AuthorMetadata>();
            var primaryAuthor = book.AuthorMetadata?.Value;
            if (primaryAuthor != null && !authors.Contains(primaryAuthor))
            {
                authors.Add(primaryAuthor);
            }

            foreach (var author in authors)
            {
                author.ForeignAuthorId = AddIdPrefix(author.ForeignAuthorId, authorPrefix);
                author.TitleSlug = AddIdPrefix(author.TitleSlug, authorPrefix);
            }

            var authorMetadata = primaryAuthor ?? authors.FirstOrDefault();
            if (authorMetadata != null)
            {
                book.AuthorMetadata = authorMetadata;
                book.Author = new Author
                {
                    Metadata = authorMetadata,
                    CleanName = Parser.Parser.CleanAuthorName(authorMetadata.Name),
                    Books = new List<Book>(),
                    Series = new List<Series>()
                };
            }

            book.ForeignBookId = AddIdPrefix(book.ForeignBookId, bookPrefix);
            book.TitleSlug = book.ForeignBookId;

            return Tuple.Create(
                AddIdPrefix(bookInfo.Item1, authorPrefix),
                book,
                authors);
        }

        private static Author NamespaceMetadataApiAuthor(Author author) => NamespaceAuthor(author, "metadata-api:", "metadata-api-author:");

        private static Author NamespaceHardcoverAuthor(Author author) => NamespaceAuthor(author, "hardcover:", "hardcover-author:");

        private static Author NamespaceAuthor(Author author, string bookPrefix, string authorPrefix)
        {
            var metadata = author.Metadata.Value;
            metadata.ForeignAuthorId = AddIdPrefix(metadata.ForeignAuthorId, authorPrefix);
            metadata.TitleSlug = AddIdPrefix(metadata.TitleSlug, authorPrefix);
            author.Metadata = metadata;

            foreach (var book in author.Books.Value)
            {
                book.ForeignBookId = AddIdPrefix(book.ForeignBookId, bookPrefix);
                book.TitleSlug = book.ForeignBookId;
                book.AuthorMetadata = metadata;
                book.Author = new Author
                {
                    Metadata = metadata,
                    CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                    Books = new List<Book>(),
                    Series = new List<Series>()
                };
            }

            return author;
        }

        private static string AddIdPrefix(string id, string prefix) =>
            id.IsNullOrWhiteSpace() || id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? id : prefix + id;

        private static bool TryGetNamespacedId(string value, string prefix, out string id)
        {
            id = null;
            if (value?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) != true)
            {
                return false;
            }

            id = value.Substring(prefix.Length);
            return !id.IsNullOrWhiteSpace();
        }

        private List<Book> Search(string query, bool getAllEditions)
        {
            List<SearchJsonResource> result;
            try
            {
                result = _goodreadsSearchProxy.Search(query);
            }
            catch (GoodreadsException e)
            {
                _logger.Warn(e, "Error searching for {0}", query?.ReplaceLineEndings(""));
                throw;
            }

            var books = new List<Book>();

            if (getAllEditions)
            {
                // Slower but more exhaustive, less intensive on metadata API
                var bookIds = result.Select(x => x.WorkId).ToList();

                var idMap = result.Select(x => new { AuthorId = x.Author.Id, BookId = x.WorkId })
                    .GroupBy(x => x.AuthorId)
                    .ToDictionary(x => x.Key, x => x.Select(i => i.BookId.ToString()).ToList());

                List<Book> authorBooks;
                foreach (var author in idMap.Keys)
                {
                    authorBooks = SearchByGoodreadsAuthorId(author);
                    books.AddRange(authorBooks.Where(b => idMap[author].Contains(b.ForeignBookId)));
                }

                var missingBooks = bookIds.ExceptBy(x => x.ToString(), books, x => x.ForeignBookId, StringComparer.Ordinal).ToList();
                foreach (var book in missingBooks)
                {
                    books.AddRange(SearchByGoodreadsWorkId(book));
                }

                return books;
            }
            else
            {
                // Use sparingly, hits metadata API quite hard
                var ids = result.Select(x => x.BookId).ToList();

                if (ids.Count == 0)
                {
                    return new List<Book>();
                }

                if (ids.Count == 1)
                {
                    return SearchByGoodreadsBookId(ids[0], false);
                }

                try
                {
                    return MapSearchResult(ids);
                }
                catch (HttpException ex)
                {
                    _logger.Warn(ex);
                    throw new BookInfoException("Search for '{0}' failed. Unable to communicate with ReadarrAPI, returning status code: {1}.", ex, query, ex.Response.StatusCode);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Error mapping search results");

                    return new List<Book>();
                }
            }
        }

        private List<Book> SearchByGoodreadsAuthorId(int id)
        {
            try
            {
                var authorId = id.ToString();
                var result = GetAuthorInfo(authorId);
                var books = result.Books.Value;
                var authors = new Dictionary<string, AuthorMetadata> { { authorId, result.Metadata.Value } };

                foreach (var book in books)
                {
                    AddDbIds(authorId, book, authors);
                }

                return books;
            }
            catch (AuthorNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Error searching by author id");
                return new List<Book>();
            }
        }

        public List<Book> SearchByGoodreadsWorkId(int id)
        {
            try
            {
                var tuple = GetBookInfo(id.ToString());
                AddDbIds(tuple.Item1, tuple.Item2, tuple.Item3.ToDictionary(x => x.ForeignAuthorId));
                return new List<Book> { tuple.Item2 };
            }
            catch (BookNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Error searching by work id");
                return new List<Book>();
            }
        }

        public List<Book> SearchByGoodreadsBookId(int id, bool getAllEditions)
        {
            try
            {
                var book = GetEditionInfo(id, getAllEditions);

                return new List<Book> { book };
            }
            catch (AuthorNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookNotFoundException)
            {
                return new List<Book>();
            }
            catch (EditionNotFoundException)
            {
                return new List<Book>();
            }
            catch (BookInfoException e)
            {
                _logger.Warn(e, "Error searching by book id");
                return new List<Book>();
            }
        }

        private Book GetEditionInfo(
            int id,
            bool getAllEditions,
            bool useNativeHardcover = true,
            bool attachDatabaseEntities = true)
        {
            if (useNativeHardcover && _hardcoverMetadataProxy.IsNativeEnabled)
            {
                var resource = _hardcoverMetadataProxy.GetEdition(id.ToString());
                var nativeBook = MapBook(resource);
                var nativeAuthors = resource.Authors.Select(MapAuthorMetadata).ToDictionary(x => x.ForeignAuthorId);
                var nativeAuthorId = GetAuthorId(resource).ToString();

                AddDbIds(nativeAuthorId, nativeBook, nativeAuthors);

                MapSeriesLinks(resource.Series.Select(MapSeries).ToList(), new List<Book> { nativeBook }, resource.Series);

                if (!getAllEditions)
                {
                    var trimmed = new Book();
                    trimmed.UseMetadataFrom(nativeBook);
                    trimmed.Author.Value.Metadata = nativeBook.AuthorMetadata.Value;
                    trimmed.AuthorMetadata = nativeBook.AuthorMetadata.Value;
                    trimmed.SeriesLinks = nativeBook.SeriesLinks;
                    var edition = nativeBook.Editions.Value.SingleOrDefault(x => x.ForeignEditionId == id.ToString());

                    if (edition == null)
                    {
                        throw new EditionNotFoundException(id.ToString());
                    }

                    edition.Monitored = true;
                    trimmed.Editions = new List<Edition> { edition };
                    nativeBook = trimmed;
                }

                return nativeBook;
            }

            HttpRequest httpRequest;
            HttpResponse httpResponse;

            while (true)
            {
                httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", $"book/{id}")
                    .Build();

                httpRequest.SuppressHttpError = true;

                // we expect a redirect
                httpResponse = _httpClient.Get(httpRequest);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    WaitUntilRetry(httpResponse);
                }
                else
                {
                    break;
                }
            }

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                throw new EditionNotFoundException(id.ToString());
            }

            if (!httpResponse.HasHttpRedirect)
            {
                throw new BookInfoException($"Unexpected response from {httpRequest.Url}");
            }

            var location = httpResponse.Headers.GetSingleValue("Location");
            var split = location.Split('/').Reverse().ToList();
            var newId = split[0];
            var type = split[1];

            Book book;
            List<AuthorMetadata> authors;

            if (type == "author")
            {
                var author = PollAuthor(newId);

                book = author.Books.Value.FirstOrDefault(b => b.Editions.Value.Any(e => e.ForeignEditionId == id.ToString()));
                authors = new List<AuthorMetadata> { author.Metadata.Value };
            }
            else if (type == "work")
            {
                var tuple = PollBook(newId);

                book = tuple.Item2;
                authors = tuple.Item3;
            }
            else
            {
                throw new NotImplementedException($"Unexpected response from {httpResponse.Request.Url}");
            }

            if (book == null || book.Editions.Value.All(e => e.ForeignEditionId != id.ToString()))
            {
                throw new EditionNotFoundException(id.ToString());
            }

            if (!getAllEditions)
            {
                var trimmed = new Book();
                trimmed.UseMetadataFrom(book);
                trimmed.Author.Value.Metadata = book.AuthorMetadata.Value;
                trimmed.AuthorMetadata = book.AuthorMetadata.Value;
                trimmed.SeriesLinks = book.SeriesLinks;
                var edition = book.Editions.Value.SingleOrDefault(e => e.ForeignEditionId == id.ToString());
                if (edition != null)
                {
                    edition.Monitored = true;
                }

                trimmed.Editions = new List<Edition> { edition };
                book = trimmed;
            }

            if (attachDatabaseEntities)
            {
                var authorDict = authors.ToDictionary(x => x.ForeignAuthorId);
                AddDbIds(book.AuthorMetadata.Value.ForeignAuthorId, book, authorDict);
            }

            return book;
        }

        private List<Book> MapSearchResult(List<int> ids, bool attachDatabaseEntities = true)
        {
            HttpResponse<BulkBookResource> httpResponse;

            while (true)
            {
                var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", "book/bulk")
                    .SetHeader("Content-Type", "application/json")
                    .Build();

                httpRequest.SetContent(ids.ToJson());
                httpRequest.ContentSummary = ids.ToJson(Formatting.None);

                httpRequest.AllowAutoRedirect = true;
                httpRequest.SuppressHttpErrorStatusCodes = new[] { HttpStatusCode.TooManyRequests };

                httpResponse = _httpClient.Post<BulkBookResource>(httpRequest);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    WaitUntilRetry(httpResponse);
                }
                else
                {
                    break;
                }
            }

            return MapBulkBook(httpResponse.Resource, attachDatabaseEntities);
        }

        private List<Book> MapBulkBook(BulkBookResource resource, bool attachDatabaseEntities = true)
        {
            var books = new List<Book>();

            if (resource == null)
            {
                return books;
            }

            var authors = resource.Authors.Select(MapAuthorMetadata).ToDictionary(x => x.ForeignAuthorId, x => x);
            var series = resource.Series.Select(MapSeries).ToList();

            foreach (var work in resource.Works)
            {
                var book = MapBook(work);
                var authorId = work.Books.OrderByDescending(b => b.AverageRating * b.RatingCount).First().Contributors.First().ForeignId.ToString();

                if (attachDatabaseEntities)
                {
                    AddDbIds(authorId, book, authors);
                }
                else if (authors.TryGetValue(authorId, out var metadata))
                {
                    book.AuthorMetadata = metadata;
                    book.Author = new Author
                    {
                        Metadata = metadata,
                        CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                        Books = new List<Book>(),
                        Series = new List<Series>()
                    };
                }

                books.Add(book);
            }

            MapSeriesLinks(series, books, resource.Series);

            return books;
        }

        private void AddDbIds(string authorId, Book book, Dictionary<string, AuthorMetadata> authors)
        {
            var dbBook = _bookService.FindById(book.ForeignBookId);
            if (dbBook != null)
            {
                book.UseDbFieldsFrom(dbBook);

                // UseDbFieldsFrom gives the book a database Id, so import no longer swaps in the
                // DB copy and UpgradeBookFile reads BookFiles straight off this instance.
                book.BookFiles = dbBook.BookFiles;

                var editions = _editionService.GetEditionsByBook(dbBook.Id).ToDictionary(x => x.ForeignEditionId);

                // If we have any database editions, exactly one will be monitored.
                // So unmonitor all the found editions and let the UseDbFieldsFrom set
                // the monitored status
                foreach (var edition in book.Editions.Value)
                {
                    edition.Monitored = false;
                    if (editions.TryGetValue(edition.ForeignEditionId, out var dbEdition))
                    {
                        edition.UseDbFieldsFrom(dbEdition);
                        edition.BookFiles = dbEdition.BookFiles;
                    }
                }

                // Double check at least one edition is monitored
                if (book.Editions.Value.Any() && !book.Editions.Value.Any(x => x.Monitored))
                {
                    var mostPopular = book.Editions.Value.OrderByDescending(x => x.Ratings.Popularity).First();
                    mostPopular.Monitored = true;
                }
            }

            var author = _authorService.FindById(authorId);

            if (author == null)
            {
                if (!authors.TryGetValue(authorId, out var metadata))
                {
                    throw new BookInfoException(string.Format("Expected author metadata for id [{0}] in book data {1}", authorId, book));
                }

                author = new Author
                {
                    CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                    Metadata = metadata
                };
            }

            book.Author = author;
            book.AuthorMetadata = author.Metadata.Value;
            book.AuthorMetadataId = author.AuthorMetadataId;
        }

        private Author PollAuthor(string foreignAuthorId)
        {
            return _authorCache.GetOrAdd(foreignAuthorId,
                () => PollAuthorUncached(foreignAuthorId),
                new LazyCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
                    ImmediateAbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
                    Size = 1,
                    SlidingExpiration = TimeSpan.FromMinutes(1),
                    ExpirationMode = ExpirationMode.ImmediateEviction
                }.RegisterPostEvictionCallback((key, value, reason, state) => _logger.Debug($"Clearing cache for {key} due to {reason}")));
        }

        private Author PollAuthorUncached(string foreignAuthorId)
        {
            AuthorResource resource = null;

            for (var i = 0; i < 60; i++)
            {
                var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", $"author/{foreignAuthorId}")
                    .Build();

                httpRequest.AllowAutoRedirect = true;
                httpRequest.SuppressHttpError = true;

                var httpResponse = _cachedHttpClient.Get(httpRequest, false, TimeSpan.FromMinutes(30));

                if (httpResponse.HasHttpError)
                {
                    if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        WaitUntilRetry(httpResponse);
                        continue;
                    }
                    else if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        throw new AuthorNotFoundException(foreignAuthorId);
                    }
                    else if (httpResponse.StatusCode == HttpStatusCode.BadRequest)
                    {
                        throw new BadRequestException(foreignAuthorId);
                    }
                    else
                    {
                        throw new BookInfoException("Unexpected error fetching author data");
                    }
                }

                resource = JsonSerializer.Deserialize<AuthorResource>(httpResponse.Content, SerializerSettings);

                if (resource.Works != null)
                {
                    resource.Works ??= new List<WorkResource>();
                    resource.Series ??= new List<SeriesResource>();
                    break;
                }

                Thread.Sleep(2000);
            }

            if (resource?.Works == null)
            {
                throw new BookInfoException($"Failed to get works for {foreignAuthorId}");
            }

            return MapAuthor(resource);
        }

        private Tuple<string, Book, List<AuthorMetadata>> PollBook(string foreignBookId)
        {
            WorkResource resource = null;

            for (var i = 0; i < 60; i++)
            {
                var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", $"work/{foreignBookId}")
                    .Build();

                httpRequest.SuppressHttpError = true;

                // this may redirect to an author
                var httpResponse = _httpClient.Get(httpRequest);

                if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    WaitUntilRetry(httpResponse);
                    continue;
                }

                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new BookNotFoundException(foreignBookId);
                }

                if (httpResponse.HasHttpRedirect)
                {
                    var location = httpResponse.Headers.GetSingleValue("Location");
                    var split = location.Split('/').Reverse().ToList();
                    var newId = split[0];
                    var type = split[1];

                    if (type == "author")
                    {
                        var author = PollAuthor(newId);
                        var authorBook = author.Books.Value.SingleOrDefault(x => x.ForeignBookId == foreignBookId);

                        if (authorBook == null)
                        {
                            throw new BookNotFoundException(foreignBookId);
                        }

                        var authorMetadata = new List<AuthorMetadata> { author.Metadata.Value };

                        return Tuple.Create(author.ForeignAuthorId, authorBook, authorMetadata);
                    }
                    else
                    {
                        throw new NotImplementedException($"Unexpected response from {httpResponse.Request.Url}");
                    }
                }

                if (httpResponse.HasHttpError)
                {
                    if (httpResponse.StatusCode == HttpStatusCode.BadRequest)
                    {
                        throw new BadRequestException(foreignBookId);
                    }
                    else
                    {
                        throw new BookInfoException("Unexpected response fetching book data");
                    }
                }

                resource = JsonSerializer.Deserialize<WorkResource>(httpResponse.Content, SerializerSettings);

                if (resource.Books != null)
                {
                    break;
                }

                Thread.Sleep(2000);
            }

            if (resource?.Books == null || resource?.Authors == null || (!resource?.Authors?.Any() ?? false))
            {
                throw new BookInfoException($"Failed to get books for {foreignBookId}");
            }

            var book = MapBook(resource);
            var authorId = GetAuthorId(resource).ToString();
            var metadata = resource.Authors.Select(MapAuthorMetadata).ToList();

            // Work responses are also used when resolving an edition. Keep the
            // primary author metadata on the mapped book so that the edition
            // path can build its trimmed book without dereferencing null.
            book.AuthorMetadata = metadata.FirstOrDefault(x => x.ForeignAuthorId == authorId) ?? metadata.First();

            var series = resource.Series.Select(MapSeries).ToList();
            MapSeriesLinks(series, new List<Book> { book }, resource.Series);

            return Tuple.Create(authorId, book, metadata);
        }

        private void WaitUntilRetry(HttpResponse response)
        {
            var seconds = 5;

            if (response.Headers.ContainsKey("Retry-After"))
            {
                var retryAfter = response.Headers["Retry-After"];

                if (!int.TryParse(retryAfter, out seconds))
                {
                    seconds = 5;
                }
            }

            _logger.Info("BookInfo returned 429, backing off for {0}s", seconds);

            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }

        private static AuthorMetadata MapAuthorMetadata(AuthorResource resource)
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = resource.ForeignId.ToString(),
                TitleSlug = resource.ForeignId.ToString(),
                Name = resource.Name.CleanSpaces(),
                Overview = resource.Description,
                Ratings = new Ratings { Votes = resource.RatingCount, Value = (decimal)resource.AverageRating },
                Status = AuthorStatusType.Continuing
            };

            metadata.SortName = metadata.Name.ToLower();
            metadata.NameLastFirst = metadata.Name.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLower();

            if (resource.ImageUrl.IsNotNullOrWhiteSpace())
            {
                metadata.Images.Add(new MediaCover.MediaCover
                {
                    Url = resource.ImageUrl,
                    CoverType = MediaCoverTypes.Poster
                });
            }

            if (resource.Url.IsNotNullOrWhiteSpace())
            {
                metadata.Links.Add(new Links { Url = resource.Url, Name = "Goodreads" });
            }

            return metadata;
        }

        private static Author MapAuthor(AuthorResource resource)
        {
            var metadata = MapAuthorMetadata(resource);

            // Author resources come from an author-scoped endpoint. Do not
            // discard a catalog work because its nested contributor IDs are
            // absent or use a different canonical-work identity.
            var books = resource.Works
                .Where(x => x.ForeignId > 0)
                .Select(MapBook)
                .ToList();

            books.ForEach(x => x.AuthorMetadata = metadata);

            var series = resource.Series.Select(MapSeries).ToList();

            MapSeriesLinks(series, books, resource.Series);

            var result = new Author
            {
                Metadata = metadata,
                CleanName = Parser.Parser.CleanAuthorName(metadata.Name),
                Books = books,
                Series = series
            };

            return result;
        }

        private static void MapSeriesLinks(List<Series> series, List<Book> books, List<SeriesResource> resource)
        {
            var bookDict = books.ToDictionary(x => x.ForeignBookId);
            var seriesDict = series.ToDictionary(x => x.ForeignSeriesId);

            foreach (var book in books)
            {
                book.SeriesLinks = new List<SeriesBookLink>();
            }

            // only take series where there are some works
            foreach (var s in resource.Where(x => x.LinkItems.Any()))
            {
                if (seriesDict.TryGetValue(s.ForeignId.ToString(), out var curr))
                {
                    curr.LinkItems = s.LinkItems.Where(x => x.ForeignWorkId != 0 && bookDict.ContainsKey(x.ForeignWorkId.ToString())).Select(l => new SeriesBookLink
                    {
                        Book = bookDict[l.ForeignWorkId.ToString()],
                        Series = curr,
                        IsPrimary = l.Primary,
                        Position = l.PositionInSeries,
                        SeriesPosition = l.SeriesPosition
                    }).ToList();

                    foreach (var l in curr.LinkItems.Value)
                    {
                        l.Book.Value.SeriesLinks.Value.Add(l);
                    }
                }
            }
        }

        private static Series MapSeries(SeriesResource resource)
        {
            var series = new Series
            {
                ForeignSeriesId = resource.ForeignId.ToString(),
                Title = resource.Title,
                Description = resource.Description
            };

            return series;
        }

        private static Book MapBook(WorkResource resource)
        {
            var book = new Book
            {
                ForeignBookId = resource.ForeignId.ToString(),
                Title = resource.Title,
                TitleSlug = resource.ForeignId.ToString(),
                CleanTitle = Parser.Parser.CleanAuthorName(resource.Title),
                ReleaseDate = resource.ReleaseDate,
                Genres = resource.Genres,
                RelatedBooks = resource.RelatedWorks
            };

            book.Links.Add(new Links { Url = resource.Url, Name = "Book Editions" });

            if (resource.Books != null)
            {
                book.Editions = resource.Books.Select(x => MapEdition(x)).ToList();

                // monitor the most popular release
                var mostPopular = book.Editions.Value.MaxBy(x => x.Ratings.Popularity);
                if (mostPopular != null)
                {
                    mostPopular.Monitored = true;

                    // fix work title if missing
                    if (book.Title.IsNullOrWhiteSpace())
                    {
                        book.Title = mostPopular.Title;
                    }
                }
            }
            else
            {
                book.Editions = new List<Edition>();
            }

            // If we are missing the book release date, set as the earliest edition release date
            if (!book.ReleaseDate.HasValue)
            {
                var editionReleases = book.Editions.Value
                    .Where(x => x.ReleaseDate.HasValue && x.ReleaseDate.Value.Month != 1 && x.ReleaseDate.Value.Day != 1)
                    .ToList();

                if (editionReleases.Any())
                {
                    book.ReleaseDate = editionReleases.Min(x => x.ReleaseDate.Value);
                }
                else
                {
                    editionReleases = book.Editions.Value.Where(x => x.ReleaseDate.HasValue).ToList();
                    if (editionReleases.Any())
                    {
                        book.ReleaseDate = editionReleases.Min(x => x.ReleaseDate.Value);
                    }
                }
            }

            Debug.Assert(!book.Editions.Value.Any() || book.Editions.Value.Count(x => x.Monitored) == 1, "one edition monitored");

            book.AnyEditionOk = true;

            var ratingCount = book.Editions.Value.Sum(x => x.Ratings.Votes);

            if (ratingCount > 0)
            {
                book.Ratings = new Ratings
                {
                    Votes = ratingCount,
                    Value = book.Editions.Value.Sum(x => x.Ratings.Votes * x.Ratings.Value) / ratingCount
                };
            }
            else
            {
                book.Ratings = new Ratings { Votes = 0, Value = 0 };
            }

            return book;
        }

        private static Edition MapEdition(BookResource resource)
        {
            var edition = new Edition
            {
                ForeignEditionId = resource.ForeignId.ToString(),
                TitleSlug = resource.ForeignId.ToString(),
                Isbn13 = resource.Isbn13,
                Asin = resource.Asin,
                Title = resource.Title.CleanSpaces(),
                Language = resource.Language,
                Overview = resource.Description,
                Format = resource.Format,
                IsEbook = resource.IsEbook,
                Disambiguation = resource.EditionInformation,
                Publisher = resource.Publisher,
                PageCount = resource.NumPages ?? 0,
                ReleaseDate = resource.ReleaseDate,
                Ratings = new Ratings { Votes = resource.RatingCount, Value = (decimal)resource.AverageRating }
            };

            if (resource.ImageUrl.IsNotNullOrWhiteSpace())
            {
                edition.Images.Add(new MediaCover.MediaCover
                {
                    Url = resource.ImageUrl,
                    CoverType = MediaCoverTypes.Cover
                });
            }

            edition.Links.Add(new Links { Url = resource.Url, Name = "Goodreads Book" });

            return edition;
        }

        private static int GetAuthorId(WorkResource b)
        {
            // Check if Books collection is null before attempting LINQ operations
            if (b.Books == null || !b.Books.Any())
            {
                return 0;
            }

            var book = b.Books.OrderByDescending(x => x.RatingCount * x.AverageRating)
                .FirstOrDefault(x => x.Contributors != null && x.Contributors.Any());
            return book?.Contributors?.FirstOrDefault()?.ForeignId ?? 0;
        }
    }
}
