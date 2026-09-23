using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Http;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    public interface IAdditionalBookMetadataProxy
    {
        List<Book> Search(string query);
        bool HandlesBookId(string foreignBookId);
        bool HandlesAuthorId(string foreignAuthorId);
        Tuple<string, Book, List<AuthorMetadata>> GetBook(string foreignBookId);
        Author GetAuthor(string foreignAuthorId);
    }

    /// <summary>
    /// Optional Google Books, Library of Congress, and Apify catalog providers.
    /// Provider-qualified foreign IDs are retained in Bookshelf so subsequent
    /// book and author refreshes resolve through the same catalog.
    /// </summary>
    public class AdditionalBookMetadataProxy : IAdditionalBookMetadataProxy
    {
        private const string GoogleBooks = "googlebooks";
        private const string LibraryOfCongress = "loc";
        private const string ApifyGoodreads = "apify-goodreads";
        private readonly IHttpClient _httpClient;
        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly ICached<List<Book>> _searchCache;
        private readonly ICached<List<JObject>> _apifyCache;
        private readonly Logger _logger;

        private static readonly TimeSpan LocRequestRateLimit = TimeSpan.FromMilliseconds(3200);

        public AdditionalBookMetadataProxy(
            IHttpClient httpClient,
            ICachedHttpResponseService cachedHttpClient,
            ICacheManager cacheManager,
            Logger logger)
        {
            _httpClient = httpClient;
            _cachedHttpClient = cachedHttpClient;
            _searchCache = cacheManager.GetCache<List<Book>>(GetType());
            _apifyCache = cacheManager.GetCache<List<JObject>>(GetType());
            _logger = logger;
        }

        private static HashSet<string> EnabledSources => AdditionalMetadataSources.GetEnabledSources(
            Environment.GetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES"),
            Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY"));

        private static bool IsEnabled(string provider) => EnabledSources.Contains(provider);

        public List<Book> Search(string query)
        {
            var cacheKey = "AdditionalMetadata:" + string.Join(",", EnabledSources.OrderBy(x => x)) + ":" + query.Trim().ToLowerInvariant();
            return _searchCache.Get(cacheKey, () => SearchUncached(query), TimeSpan.FromMinutes(10));
        }

        private List<Book> SearchUncached(string query)
        {
            var books = new List<Book>();
            foreach (var provider in EnabledSources)
            {
                try
                {
                    if (provider == GoogleBooks)
                    {
                        books.AddRange(SearchGoogleBooks(query));
                    }
                    else if (provider == LibraryOfCongress)
                    {
                        books.AddRange(SearchLoc(query));
                    }
                    else if (provider == ApifyGoodreads)
                    {
                        books.AddRange(SearchApify(query));
                    }
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Additional book metadata provider {0} failed", provider);
                }
            }

            return books
                .Where(x => !x.ForeignBookId.IsNullOrWhiteSpace())
                .DistinctBy(x => x.ForeignBookId)
                .ToList();
        }

        public bool HandlesBookId(string foreignBookId) =>
            HasPrefix(foreignBookId, GoogleBooks) ||
            HasPrefix(foreignBookId, LibraryOfCongress) ||
            HasPrefix(foreignBookId, ApifyGoodreads);

        public bool HandlesAuthorId(string foreignAuthorId) =>
            HasPrefix(foreignAuthorId, GoogleBooks + "-author") ||
            HasPrefix(foreignAuthorId, LibraryOfCongress + "-author") ||
            HasPrefix(foreignAuthorId, ApifyGoodreads + "-author");

        public Tuple<string, Book, List<AuthorMetadata>> GetBook(string foreignBookId)
        {
            var (provider, id) = ParseId(foreignBookId);
            JObject record;
            if (provider == GoogleBooks)
            {
                var key = Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY");
                if (key.IsNullOrWhiteSpace())
                {
                    throw new BookInfoException("Google Books requires GOOGLE_BOOKS_API_KEY.");
                }

                var url = new HttpRequestBuilder("https://www.googleapis.com/books/v1/volumes/{id}")
                    .SetSegment("id", id)
                    .AddQueryParam("key", key)
                    .Build();
                record = _cachedHttpClient.Get<JObject>(url, false, TimeSpan.FromDays(1)).Resource;
            }
            else if (provider == LibraryOfCongress)
            {
                var itemUrl = DecodeLocUrl(id);
                var uri = new UriBuilder(itemUrl) { Query = "fo=json" };
                record = _cachedHttpClient.Get<JObject>(
                    new HttpRequestBuilder(uri.Uri.ToString())
                        .WithRateLimit(LocRequestRateLimit.TotalSeconds)
                        .Build(),
                    true,
                    TimeSpan.FromDays(1)).Resource;
                record = record["item"] as JObject ?? record;
            }
            else if (provider == ApifyGoodreads)
            {
                var source = DecodeApifyId(id);
                var results = RunApify((string)source["query"]);
                record = (results.FirstOrDefault(x => GetApifyStableId(x) == (string)source["recordId"])
                    ?? results.FirstOrDefault(x => NormalizeText((string)x["title"]) == (string)source["title"])) ?? throw new BookNotFoundException(foreignBookId);
            }
            else
            {
                throw new BookNotFoundException(foreignBookId);
            }

            var book = provider == GoogleBooks ? MapGoogleVolume(record) :
                provider == LibraryOfCongress ? MapLocRecord(record) : MapApifyRecord(record);
            var metadata = book.AuthorMetadata.Value;
            book.Author.Value.Metadata = metadata;
            return Tuple.Create(metadata.ForeignAuthorId, book, new List<AuthorMetadata> { metadata });
        }

        public Author GetAuthor(string foreignAuthorId)
        {
            var (provider, encodedName) = ParseId(foreignAuthorId);
            var name = Decode(encodedName);
            var books = Search(name).Where(x =>
                x.AuthorMetadata.Value.ForeignAuthorId == foreignAuthorId).ToList();
            var metadata = MakeAuthorMetadata(foreignAuthorId, name, null, null);
            books.ForEach(x => x.AuthorMetadata = metadata);
            var author = new Author
            {
                CleanName = Parser.Parser.CleanAuthorName(name),
                Books = books,
                Series = new List<Series>(),
                Metadata = metadata
            };
            return author;
        }

        private List<Book> SearchGoogleBooks(string query)
        {
            var key = Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY");
            if (key.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            var request = new HttpRequestBuilder("https://www.googleapis.com/books/v1/volumes")
                .AddQueryParam("q", query)
                .AddQueryParam("maxResults", "10")
                .AddQueryParam("key", key)
                .Build();
            var response = _cachedHttpClient.Get<JObject>(request, true, TimeSpan.FromDays(1)).Resource;
            return (response["items"] as JArray ?? new JArray())
                .OfType<JObject>().Select(MapGoogleVolume).Where(x => x != null).ToList();
        }

        private List<Book> SearchLoc(string query)
        {
            var request = new HttpRequestBuilder("https://www.loc.gov/books/")
                .AddQueryParam("q", query)
                .AddQueryParam("fo", "json")
                .AddQueryParam("c", "10")
                .WithRateLimit(LocRequestRateLimit.TotalSeconds)
                .Build();
            var response = _cachedHttpClient.Get<JObject>(request, true, TimeSpan.FromDays(1)).Resource;
            return (response["results"] as JArray ?? new JArray())
                .OfType<JObject>().Select(MapLocRecord).Where(x => x != null).ToList();
        }

        private List<Book> SearchApify(string query)
        {
            if (Environment.GetEnvironmentVariable("HARDCOVER_APIFY_GOODREADS_ACTOR").IsNullOrWhiteSpace() ||
                Environment.GetEnvironmentVariable("HARDCOVER_APIFY_TOKEN").IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            return RunApify(query).Select(MapApifyRecord).Where(x => x != null).ToList();
        }

        private List<JObject> RunApify(string query)
        {
            return _apifyCache.Get(
                "apify:" + query.Trim().ToLowerInvariant(),
                () => RunApifyUncached(query),
                TimeSpan.FromDays(1));
        }

        private List<JObject> RunApifyUncached(string query)
        {
            var actor = Environment.GetEnvironmentVariable("HARDCOVER_APIFY_GOODREADS_ACTOR");
            var token = Environment.GetEnvironmentVariable("HARDCOVER_APIFY_TOKEN");
            if (actor.IsNullOrWhiteSpace() || token.IsNullOrWhiteSpace())
            {
                return new List<JObject>();
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(actor, "^(?:[A-Za-z0-9_-]+~[A-Za-z0-9_-]+|[A-Za-z0-9_-]{10,64})$"))
            {
                throw new BookInfoException("Invalid Apify actor identifier.");
            }

            var template = Environment.GetEnvironmentVariable("HARDCOVER_APIFY_GOODREADS_INPUT_TEMPLATE") ?? "{\"searchQueries\":[{{query}}],\"maxItems\":10}";
            if (!template.Contains("{{query}}") || template.Length > 16384)
            {
                throw new BookInfoException("Apify input template must be valid JSON and include {{query}}.");
            }

            var input = template.Replace("{{query}}", Newtonsoft.Json.JsonConvert.ToString(query));
            var request = new HttpRequestBuilder("https://api.apify.com/v2/acts/{actor}/run-sync-get-dataset-items")
                .SetSegment("actor", actor)
                .SetHeader("Authorization", "Bearer " + token)
                .SetHeader("Content-Type", "application/json")
                .Build();
            request.Method = HttpMethod.Post;
            request.SetContent(input);
            return (_httpClient.Post<JArray>(request).Resource ?? new JArray())
                .OfType<JObject>().Take(25).ToList();
        }

        private Book MapGoogleVolume(JObject volume)
        {
            if (volume["volumeInfo"] is not JObject info)
            {
                return null;
            }

            var id = (string)volume["id"];
            var authors = info["authors"] as JArray;
            var author = authors?.Values<string>().FirstOrDefault();
            return BuildBook(
                GoogleBooks,
                id,
                (string)info["title"],
                author,
                (string)info["description"],
                (string)info["publisher"],
                (string)info["language"],
                (string)info["publishedDate"],
                (int?)info["pageCount"],
                (string)info["imageLinks"]?["thumbnail"] ?? (string)info["imageLinks"]?["smallThumbnail"],
                info["industryIdentifiers"] as JArray,
                "https://books.google.com/books?id=" + Uri.EscapeDataString(id));
        }

        private Book MapLocRecord(JObject record)
        {
            var idUrl = (string)record["id"];
            var uri = SafeLocUri(idUrl);
            var author = (record["contributors"] as JArray)?.Values<string>().FirstOrDefault()
                ?? (record["contributor"] as JArray)?.Values<string>().FirstOrDefault();
            return BuildBook(
                LibraryOfCongress,
                Encode(uri.ToString()),
                (string)record["title"],
                author,
                (record["description"] as JArray)?.Values<string>().FirstOrDefault() ?? (string)record["description"],
                (record["publisher"] as JArray)?.Values<string>().FirstOrDefault(),
                null,
                (string)record["date"],
                null,
                (string)record["image_url"],
                record["identifiers"] as JArray,
                uri.ToString());
        }

        private Book MapApifyRecord(JObject record)
        {
            if (record == null)
            {
                return null;
            }

            var authorValue = record["author"] ?? record["authors"] ?? record["authorName"];
            var author = authorValue is JArray list ? list.Values<string>().FirstOrDefault() : (string)authorValue;
            var title = (string)record["title"] ?? (string)record["fullTitle"] ?? (string)record["bookTitle"];
            var sourceId = GetApifyStableId(record);
            var id = Encode(Newtonsoft.Json.JsonConvert.SerializeObject(new { query = title + " " + author, recordId = sourceId, title }));
            var identifiers = new JArray(new[] { record["isbn13"], record["isbn_13"], record["isbn"], record["isbn10"] }.Where(x => x != null));
            return BuildBook(
                ApifyGoodreads,
                id,
                title,
                author,
                (string)record["description"] ?? (string)record["overview"],
                (string)record["publisher"],
                (string)record["language"],
                (string)record["publishedDate"] ?? (string)record["publicationDate"],
                (int?)record["pages"] ?? (int?)record["pageCount"],
                (string)record["coverImage"] ?? (string)record["cover_image"] ?? (string)record["imageUrl"],
                identifiers,
                (string)record["url"] ?? "https://www.goodreads.com/");
        }

        private Book BuildBook(
            string provider,
            string id,
            string title,
            string author,
            string description,
            string publisher,
            string language,
            string date,
            int? pages,
            string image,
            JArray identifiers,
            string url)
        {
            if (title.IsNullOrWhiteSpace() || author.IsNullOrWhiteSpace() || id.IsNullOrWhiteSpace())
            {
                return null;
            }

            var providerId = provider == LibraryOfCongress ? "loc:" + id : provider + ":" + id;
            var authorId = provider + "-author:" + Encode(author);
            var dateValue = DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate)
                ? parsedDate : (DateTime?)null;
            var isbn = identifiers?.Select(GetIdentifierValue).FirstOrDefault(Isbn13IsValid);
            var authorMetadata = MakeAuthorMetadata(authorId, author, null, null);
            var edition = new Edition
            {
                ForeignEditionId = providerId,
                TitleSlug = providerId,
                Title = title,
                Isbn13 = isbn,
                Overview = description ?? string.Empty,
                Publisher = publisher,
                Language = language,
                PageCount = pages ?? 0,
                ReleaseDate = dateValue,
                Monitored = true
            };
            if (!image.IsNullOrWhiteSpace() && IsAllowedImage(image, provider))
            {
                edition.Images.Add(new MediaCover.MediaCover { Url = image, CoverType = MediaCoverTypes.Cover });
            }

            var book = new Book
            {
                ForeignBookId = providerId,
                TitleSlug = providerId,
                Title = title.CleanSpaces(),
                CleanTitle = Parser.Parser.CleanAuthorName(title),
                ReleaseDate = dateValue,
                AnyEditionOk = true,
                Editions = new List<Edition> { edition },
                AuthorMetadata = authorMetadata,
                Author = new Author { CleanName = Parser.Parser.CleanAuthorName(author), Metadata = authorMetadata },
                AuthorMetadataId = 0
            };
            book.Links.Add(new Links { Name = provider, Url = url });
            return book;
        }

        private static AuthorMetadata MakeAuthorMetadata(string id, string name, string description, string image)
        {
            var metadata = new AuthorMetadata
            {
                ForeignAuthorId = id,
                TitleSlug = id,
                Name = name.CleanSpaces(),
                Overview = description,
                Status = AuthorStatusType.Continuing
            };
            metadata.SortName = metadata.Name.ToLowerInvariant();
            metadata.NameLastFirst = metadata.Name.ToLastFirst();
            metadata.SortNameLastFirst = metadata.NameLastFirst.ToLowerInvariant();
            if (!image.IsNullOrWhiteSpace() && Uri.TryCreate(image, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                metadata.Images.Add(new MediaCover.MediaCover { Url = uri.ToString(), CoverType = MediaCoverTypes.Poster });
            }

            return metadata;
        }

        private static bool IsAllowedImage(string value, string provider)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            var host = uri.Host.ToLowerInvariant();
            return provider == GoogleBooks ? host == "books.google.com" || host.EndsWith(".googleusercontent.com") :
                provider == LibraryOfCongress ? host == "loc.gov" || host.EndsWith(".loc.gov") :
                host == "goodreads.com" || host.EndsWith(".gr-assets.com") || host.EndsWith(".ssl-images-amazon.com");
        }

        private static bool Isbn13IsValid(string value) => value != null && System.Text.RegularExpressions.Regex.IsMatch(value, "^97[89][0-9]{10}$");
        private static string GetIdentifierValue(JToken item) => item is JObject obj ? (string)obj["identifier"] : item?.ToString();
        private static string GetApifyStableId(JObject record) => (string)record["goodreadsId"] ?? (string)record["goodreads_id"] ?? (string)record["bookId"] ?? (string)record["id"] ?? (string)record["isbn13"] ?? (string)record["isbn_13"];
        private static bool HasPrefix(string value, string provider) => value?.StartsWith(provider + ":", StringComparison.OrdinalIgnoreCase) == true;
        private static (string Provider, string Id) ParseId(string value)
        {
            var index = value?.IndexOf(':') ?? -1;
            if (index <= 0)
            {
                throw new BookNotFoundException(value);
            }

            return (value[..index], value[(index + 1) ..]);
        }

        private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - (value.Length % 4)) % 4)));
        private static string NormalizeText(string value) => (value ?? string.Empty).Trim();
        private static JObject DecodeApifyId(string value)
        {
            var json = Decode(value);
            return JObject.Parse(json);
        }

        private static Uri DecodeLocUrl(string value) => SafeLocUri(Decode(value));
        private static Uri SafeLocUri(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !(uri.Host.Equals("www.loc.gov", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("loc.gov", StringComparison.OrdinalIgnoreCase)))
            {
                throw new BookInfoException("Library of Congress returned an invalid record URL.");
            }

            return uri;
        }
    }
}
