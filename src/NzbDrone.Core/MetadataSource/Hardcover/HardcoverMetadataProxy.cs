using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Goodreads;
using AuthorResource = NzbDrone.Core.MetadataSource.BookInfo.AuthorResource;
using BookResource = NzbDrone.Core.MetadataSource.BookInfo.BookResource;
using SeriesResource = NzbDrone.Core.MetadataSource.BookInfo.SeriesResource;
using WorkResource = NzbDrone.Core.MetadataSource.BookInfo.WorkResource;

namespace NzbDrone.Core.MetadataSource.Hardcover
{
    public interface IHardcoverMetadataProxy
    {
        bool IsConfigured { get; }
        bool IsNativeEnabled { get; }
        List<SearchJsonResource> Search(string query);
        AuthorResource GetAuthor(string foreignAuthorId);
        WorkResource GetWork(string foreignWorkId);
        WorkResource GetEdition(string foreignEditionId);
    }

    /// <summary>
    /// Native Hardcover metadata access for the Hardcover Bookshelf image.
    ///
    /// The legacy Readarr metadata API remains available through BookInfoProxy
    /// when native mode is disabled. A native deployment without a token fails
    /// clearly instead of silently switching metadata providers. This class
    /// deliberately returns the existing Bookshelf metadata resource shapes so
    /// native and compatibility mode share the same domain mapping.
    /// </summary>
    public class HardcoverMetadataProxy : IHardcoverMetadataProxy
    {
        private const string DefaultApiUrl = "https://api.hardcover.app";
        private const int CacheLockCount = 32;
        private const int MaxRequestAttempts = 3;
        private const int AuthorPageSize = 50;
        private static readonly Regex AsinRegex = new Regex("^[Bb]0[0-9A-Za-z]{8}$", RegexOptions.Compiled);
        private static readonly Regex Isbn10Regex = new Regex("^[0-9]{9}[0-9Xx]$", RegexOptions.Compiled);
        private static readonly Regex Isbn13Regex = new Regex("^[0-9]{13}$", RegexOptions.Compiled);
        private static readonly object[] CacheLocks = Enumerable.Range(0, CacheLockCount).Select(x => new object()).ToArray();
        private static readonly HashSet<string> NonPrimaryRoles = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "pseudonym",
            "translator",
            "narrator",
            "reading",
            "adaptation",
            "illustrator",
            "illustrations",
            "ilustrator",
            "contributor & illustrator",
            "writer/illustrator",
            "writer, illustrator",
            "writer, editor",
            "brand",
            "visual art",
            "character design",
            "artist",
            "cover",
            "cover art",
            "cover artist",
            "text",
            "writer",
            "writer, editior",
            "author & editor",
            "penciler",
            "penciller",
            "inker",
            "colourist",
            "letterer",
            "colorist",
            "contributor",
            "contributer",
            "guion",
            "dibujo",
            "foreword",
            "foreward",
            "introduction",
            "introduction/contributor",
            "editor/introduction",
            "editor",
            "editor and contributor",
            "editor/contributor",
            "editor / contributor",
            "editor,contributor"
        };

        private static readonly string SearchQuery = @"
query Search($query: String!) {
  search(
    query: $query
    per_page: 15
    query_type: ""book""
    fields: ""title,isbns,series_names,author_names,alternative_titles""
    weights: ""5,1,3,5,1""
    sort: ""ratings_count:desc,_text_match:desc""
  ) {
    ids
  }
}";

        private static readonly string IdentifierQuery = @"
query GetWorkByASINISBN($identifier: String!) {
  editions(
    where: {
      _or: [
        { isbn_10: { _eq: $identifier } }
        { isbn_13: { _eq: $identifier } }
        { asin: { _eq: $identifier } }
      ]
    }
  ) {
    id
    book_id
  }
}";

        private static readonly string WorkQuery = @"
query GetWork($bookID: Int!) {
  books_by_pk(id: $bookID) {
    id
    title
    subtitle
    description
    release_date
    cached_tags(path: ""$.Genre"")
    cached_image(path: ""url"")
    slug
    canonical_id
    rating
    ratings_count
    contributions {
      contribution
      author {
        id
        name
        slug
        bio
        cached_image(path: ""url"")
      }
    }
    book_series {
      position
      series {
        id
        name
        description
      }
    }
    editions(order_by: { score: desc_nulls_last }) {
      id
      title
      subtitle
      asin
      isbn_13
      edition_format
      pages
      language { code3 }
      publisher { name }
      release_date
      physical_format
      physical_information
      edition_information
    }
  }
}";

        private static readonly string EditionQuery = @"
query GetEdition($editionID: Int!) {
  editions_by_pk(id: $editionID) {
    book { id }
  }
}";

        private static readonly string AuthorQuery = @"
query GetAuthorEditions($id: Int!, $limit: Int!, $offset: Int!) {
  authors_by_pk(id: $id) {
    id
    name
    slug
    bio
    cached_image(path: ""url"")
    contributions(
      limit: $limit
      offset: $offset
      order_by: { book: { ratings_count: desc } }
      where: {
        contributable_type: { _eq: ""Book"" }
        book: { book_status_id: { _eq: ""1"" } }
      }
    ) {
      contribution
      author {
        id
        name
        slug
        bio
        cached_image(path: ""url"")
      }
      book {
        id
        title
        subtitle
        description
        release_date
        cached_tags(path: ""$.Genre"")
        cached_image(path: ""url"")
        slug
        canonical_id
        rating
        ratings_count
        contributions {
          contribution
          author {
            id
            name
            slug
            bio
            cached_image(path: ""url"")
          }
        }
        book_series {
          position
          series {
            id
            name
            description
          }
        }
        editions(order_by: { score: desc_nulls_last }) {
          id
          title
          subtitle
          asin
          isbn_13
          edition_format
          pages
          language { code3 }
          publisher { name }
          release_date
          physical_format
          physical_information
          edition_information
        }
      }
    }
  }
}";

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly ICached<WorkResource> _workCache;
        private readonly ICached<AuthorResource> _authorCache;
        private readonly ICached<List<SearchJsonResource>> _searchCache;

        public HardcoverMetadataProxy(IHttpClient httpClient,
                                      Logger logger,
                                      ICacheManager cacheManager)
        {
            _httpClient = httpClient;
            _logger = logger;
            _workCache = cacheManager.GetCache<WorkResource>(GetType(), "works");
            _authorCache = cacheManager.GetCache<AuthorResource>(GetType(), "authors");
            _searchCache = cacheManager.GetCache<List<SearchJsonResource>>(GetType(), "search");
        }

        public bool IsConfigured => !GetApiToken().IsNullOrWhiteSpace();

        // Mode selection is intentionally independent from credentials. A
        // native deployment with a missing token must fail clearly at request
        // time instead of silently switching to METADATA_URL.
        public bool IsNativeEnabled =>
            string.Equals(Environment.GetEnvironmentVariable("HARDCOVER"), "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment.GetEnvironmentVariable("HARDCOVER_NATIVE"), "false", StringComparison.OrdinalIgnoreCase);

        public List<SearchJsonResource> Search(string query)
        {
            if (query.IsNullOrWhiteSpace())
            {
                return new List<SearchJsonResource>();
            }

            var normalized = query.Trim();
            var cacheKey = normalized.ToLowerInvariant();

            return GetCached(_searchCache, cacheKey, () => SearchUncached(normalized), TimeSpan.FromMinutes(10));
        }

        public AuthorResource GetAuthor(string foreignAuthorId)
        {
            var id = ParseId(foreignAuthorId, "author");

            var cacheKey = id.ToString(CultureInfo.InvariantCulture);
            return GetCached(_authorCache, cacheKey, () => LoadAuthor(id), TimeSpan.FromMinutes(30));
        }

        public WorkResource GetWork(string foreignWorkId)
        {
            var id = ParseId(foreignWorkId, "work");

            var cacheKey = id.ToString(CultureInfo.InvariantCulture);
            return GetCached(_workCache, cacheKey, () => LoadWork(id), TimeSpan.FromHours(6));
        }

        public WorkResource GetEdition(string foreignEditionId)
        {
            var id = ParseId(foreignEditionId, "edition");
            var data = ExecuteGraphQl("GetEdition", EditionQuery, new JObject { ["editionID"] = id });
            var edition = data["editions_by_pk"] as JObject;
            var workId = GetInt(AsObject(edition?["book"])?["id"]);

            if (workId == 0)
            {
                throw new EditionNotFoundException(foreignEditionId);
            }

            return GetWork(workId.ToString(CultureInfo.InvariantCulture));
        }

        private List<SearchJsonResource> SearchUncached(string query)
        {
            if (LooksLikeIsbnOrAsin(query))
            {
                return SearchByIdentifier(query.Replace("-", string.Empty).Replace(" ", string.Empty));
            }

            var data = ExecuteGraphQl("Search", SearchQuery, new JObject { ["query"] = query });
            var ids = AsObject(data["search"])?["ids"] as JArray;
            var results = new List<SearchJsonResource>();

            if (ids == null)
            {
                return results;
            }

            foreach (var idToken in ids)
            {
                var workId = GetInt(idToken);
                if (workId == 0)
                {
                    continue;
                }

                try
                {
                    var work = GetWork(workId.ToString(CultureInfo.InvariantCulture));
                    var edition = work.Books?.FirstOrDefault();
                    var author = work.Authors?.FirstOrDefault();

                    if (edition == null || author == null)
                    {
                        continue;
                    }

                    results.Add(ToSearchResource(work, edition, author));
                }
                catch (BookInfoException ex)
                {
                    _logger.Warn(ex, "Unable to map Hardcover work {0} returned for search {1}", workId, query);
                }
            }

            return results;
        }

        private List<SearchJsonResource> SearchByIdentifier(string identifier)
        {
            var data = ExecuteGraphQl("GetWorkByASINISBN", IdentifierQuery, new JObject { ["identifier"] = identifier });
            var editions = data["editions"] as JArray;
            var results = new List<SearchJsonResource>();

            if (editions == null)
            {
                return results;
            }

            foreach (var editionToken in editions.OfType<JObject>())
            {
                var workId = GetInt(editionToken["book_id"]);
                var editionId = GetInt(editionToken["id"]);
                if (workId == 0 || editionId == 0)
                {
                    continue;
                }

                try
                {
                    var work = GetWork(workId.ToString(CultureInfo.InvariantCulture));
                    var edition = work.Books?.FirstOrDefault(x => x.ForeignId == editionId);
                    var author = work.Authors?.FirstOrDefault();

                    if (edition != null && author != null)
                    {
                        results.Add(ToSearchResource(work, edition, author));
                    }
                }
                catch (BookInfoException ex)
                {
                    _logger.Warn(ex, "Unable to map Hardcover edition {0} returned for identifier {1}", editionId, identifier);
                }
            }

            return results;
        }

        private AuthorResource LoadAuthor(int id)
        {
            JObject author = null;
            var works = new List<WorkResource>();
            var offset = 0;

            while (true)
            {
                var data = ExecuteGraphQl("GetAuthorEditions", AuthorQuery, new JObject
                {
                    ["id"] = id,
                    ["limit"] = AuthorPageSize,
                    ["offset"] = offset
                });

                author = author ?? data["authors_by_pk"] as JObject;
                var pageAuthor = data["authors_by_pk"] as JObject;
                var contributions = pageAuthor?["contributions"] as JArray ?? new JArray();

                foreach (var contribution in contributions.OfType<JObject>())
                {
                    var book = contribution["book"] as JObject;
                    var workId = GetInt(book?["id"]);
                    if (workId == 0)
                    {
                        continue;
                    }

                    try
                    {
                        var work = book?["editions"] is JArray ? MapWork(book) : GetWork(workId.ToString(CultureInfo.InvariantCulture));
                        if (GetAuthorIds(work).Contains(id))
                        {
                            works.Add(work);
                            _workCache.Set(workId.ToString(CultureInfo.InvariantCulture), work, TimeSpan.FromHours(6));
                        }
                    }
                    catch (BookInfoException ex)
                    {
                        _logger.Warn(ex, "Unable to load Hardcover work {0} for author {1}", workId, id);
                    }
                }

                if (contributions.Count < AuthorPageSize)
                {
                    break;
                }

                offset += AuthorPageSize;
            }

            if (author == null)
            {
                throw new AuthorNotFoundException(id.ToString(CultureInfo.InvariantCulture));
            }

            var result = MapAuthor(author);

            result.Works = works
                .GroupBy(x => x.ForeignId)
                .Select(x => x.First())
                .ToList();
            result.Series = result.Works
                .SelectMany(x => x.Series ?? new List<SeriesResource>())
                .GroupBy(x => x.ForeignId)
                .Select(x => x.First())
                .ToList();

            return result;
        }

        private WorkResource LoadWork(int id)
        {
            var data = ExecuteGraphQl("GetWork", WorkQuery, new JObject { ["bookID"] = id });
            var work = data["books_by_pk"] as JObject;

            if (work == null)
            {
                throw new BookNotFoundException(id.ToString(CultureInfo.InvariantCulture));
            }

            var canonicalId = GetInt(work["canonical_id"]);
            if (canonicalId != 0 && canonicalId != id)
            {
                return GetWork(canonicalId.ToString(CultureInfo.InvariantCulture));
            }

            return MapWork(work);
        }

        private WorkResource MapWork(JObject work)
        {
            var workId = GetInt(work["id"]);
            var authors = SelectAuthors(work["contributions"] as JArray);

            if (workId == 0 || !authors.Any())
            {
                throw new BookInfoException("Hardcover returned a work without an id or author");
            }

            var series = (work["book_series"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(x => MapSeries(x, workId))
                .Where(x => x != null)
                .ToList();

            var books = (work["editions"] as JArray ?? new JArray())
                .OfType<JObject>()
                .Select(x => MapEdition(x, work))
                .Where(x => x != null)
                .ToList();

            var authorResources = authors.Select(MapAuthor).ToList();
            foreach (var authorResource in authorResources)
            {
                authorResource.Series = series;
            }

            return new WorkResource
            {
                ForeignId = workId,
                Title = CleanSubtitle(GetString(work["title"]), GetString(work["subtitle"])),
                Url = HardcoverUrl("books", GetString(work["slug"]), workId),
                ReleaseDate = ParseDate(GetString(work["release_date"])),
                Genres = GetGenres(work["cached_tags"]),
                RelatedWorks = new List<int>(),
                Books = books,
                Series = series,
                Authors = authorResources
            };
        }

        private static BookResource MapEdition(JObject edition, JObject work)
        {
            var editionId = GetInt(edition["id"]);
            if (editionId == 0)
            {
                return null;
            }

            var title = GetString(edition["title"]);
            var subtitle = GetString(edition["subtitle"]);
            var workId = GetInt(work["id"]);
            var format = GetString(edition["edition_format"]);
            var slug = GetString(work["slug"]);
            var contributors = SelectAuthorContributions(work["contributions"] as JArray)
                .Select(x => new ContributorResource
                {
                    ForeignId = GetInt(AsObject(x["author"])?["id"]),
                    Role = WithFallback(GetString(x["contribution"]), "Author")
                })
                .ToList();

            return new BookResource
            {
                ForeignId = editionId,
                Asin = GetString(edition["asin"]),
                Description = WithFallback(GetString(work["description"]), "N/A"),
                Isbn13 = GetString(edition["isbn_13"]),
                Title = CleanSubtitle(title, subtitle),
                Language = GetString(AsObject(edition["language"])?["code3"]),
                Format = format.IsNullOrWhiteSpace() ? GetString(edition["physical_format"]) : format,
                EditionInformation = WithFallback(GetString(edition["edition_information"]), GetString(edition["physical_information"])),
                Publisher = GetString(AsObject(edition["publisher"])?["name"]),
                ImageUrl = GetString(work["cached_image"]),
                IsEbook = string.Equals(format, "ebook", StringComparison.OrdinalIgnoreCase) || string.Equals(format, "kindle edition", StringComparison.OrdinalIgnoreCase),
                NumPages = GetNullableInt(edition["pages"]),
                RatingCount = GetInt(work["ratings_count"]),
                AverageRating = GetDouble(work["rating"]),
                Url = HardcoverUrl("books", slug, workId),
                ReleaseDate = ParseDate(GetString(edition["release_date"])),
                Contributors = contributors
            };
        }

        private static AuthorResource MapAuthor(JObject author)
        {
            var id = GetInt(author["id"]);
            var name = GetString(author["name"]);

            return new AuthorResource
            {
                ForeignId = id,
                Name = name,
                Description = WithFallback(GetString(author["bio"]), "N/A"),
                ImageUrl = GetString(author["cached_image"]),
                Url = HardcoverUrl("authors", GetString(author["slug"]), id),
                RatingCount = 0,
                AverageRating = 0,
                Works = new List<WorkResource>(),
                Series = new List<SeriesResource>()
            };
        }

        private static SeriesResource MapSeries(JObject bookSeries, int workId)
        {
            var series = bookSeries["series"] as JObject;
            var seriesId = GetInt(series?["id"]);
            if (seriesId == 0)
            {
                return null;
            }

            var position = GetDouble(bookSeries["position"]);

            return new SeriesResource
            {
                ForeignId = seriesId,
                Title = GetString(series["name"]),
                Description = GetString(series["description"]),
                LinkItems = new List<SeriesWorkLinkResource>
                {
                    new SeriesWorkLinkResource
                    {
                        ForeignWorkId = workId,
                        PositionInSeries = position.ToString(CultureInfo.InvariantCulture),
                        SeriesPosition = (int)position,
                        Primary = false
                    }
                }
            };
        }

        private static SearchJsonResource ToSearchResource(WorkResource work, BookResource edition, AuthorResource author)
        {
            return new SearchJsonResource
            {
                BookId = edition.ForeignId,
                WorkId = work.ForeignId,
                BookUrl = edition.Url,
                ImageUrl = edition.ImageUrl,
                Title = work.Title,
                BookTitleBare = edition.Title,
                PageCount = edition.NumPages ?? 0,
                AverageRating = (decimal)edition.AverageRating,
                RatingsCount = edition.RatingCount,
                Author = new AuthorJsonResource
                {
                    Id = author.ForeignId,
                    Name = author.Name,
                    ProfileUrl = author.Url,
                    IsGoodreadsAuthor = false
                }
            };
        }

        private JObject ExecuteGraphQl(string operationName, string query, JObject variables)
        {
            if (!IsConfigured)
            {
                throw new BookInfoException("Native Hardcover metadata requires HARDCOVER_AUTH or HARDCOVER_API_KEY");
            }

            var payload = new JObject
            {
                ["operationName"] = operationName,
                ["query"] = query,
                ["variables"] = variables
            }.ToString(Formatting.None);

            for (var attempt = 0; attempt < MaxRequestAttempts; attempt++)
            {
                var request = BuildGraphQlRequest(payload);
                HttpResponse response;

                try
                {
                    response = _httpClient.Execute(request);
                }
                catch (WebException ex)
                {
                    if (attempt < MaxRequestAttempts - 1 && IsTransientNetworkError(ex))
                    {
                        WaitUntilRetry(null, attempt, ex.Status.ToString());
                        continue;
                    }

                    throw new BookInfoException("Hardcover {0} network request failed", ex, operationName);
                }

                if (IsTransientStatus(response.StatusCode))
                {
                    if (attempt < MaxRequestAttempts - 1)
                    {
                        WaitUntilRetry(response, attempt, response.StatusCode.ToString());
                        continue;
                    }

                    throw new BookInfoException("Hardcover returned HTTP status {0} for {1} after {2} attempts", response.StatusCode, operationName, MaxRequestAttempts);
                }

                if (response.HasHttpError)
                {
                    throw new BookInfoException("Hardcover returned HTTP status {0} for {1}", response.StatusCode, operationName);
                }

                if (response.Content.IsNullOrWhiteSpace())
                {
                    throw new BookInfoException("Hardcover {0} returned an empty response", operationName);
                }

                JObject root;
                try
                {
                    root = JObject.Parse(response.Content);
                }
                catch (JsonException ex)
                {
                    throw new BookInfoException("Hardcover {0} returned invalid JSON", ex, operationName);
                }

                var errors = root["errors"] as JArray;
                if (errors != null && errors.Count > 0)
                {
                    var firstError = errors.FirstOrDefault() as JObject;
                    var message = firstError?["message"]?.Value<string>() ?? "GraphQL request failed";
                    throw new BookInfoException("Hardcover {0} failed: {1}", operationName, message);
                }

                return root["data"] as JObject ?? throw new BookInfoException("Hardcover {0} returned no data", operationName);
            }

            throw new BookInfoException("Hardcover {0} request failed", operationName);
        }

        private HttpRequest BuildGraphQlRequest(string payload)
        {
            var request = new HttpRequestBuilder($"{GetApiUrl().TrimEnd('/')}/v1/graphql")
                .Post()
                .Accept(HttpAccept.Json)
                .SetHeader("Authorization", $"Bearer {GetApiToken()}")
                .SetHeader("X-Api-Key", GetApiToken())
                .SetHeader("User-Agent", "BookshelfNG (Hardcover metadata)")
                .SetHeader("Content-Type", "application/json")
                .KeepAlive()
                .Build();

            request.SuppressHttpError = true;
            request.SetContent(payload);
            request.ContentSummary = "Hardcover GraphQL request";
            return request;
        }

        private void WaitUntilRetry(HttpResponse response, int attempt, string reason)
        {
            var seconds = Math.Min(1 << attempt, 4);
            if (response != null && response.StatusCode == HttpStatusCode.TooManyRequests &&
                response.Headers.ContainsKey("Retry-After") && int.TryParse(response.Headers["Retry-After"], out var retryAfter))
            {
                seconds = Math.Min(Math.Max(retryAfter, 1), 30);
            }

            _logger.Info("Hardcover returned {0}, retrying in {1}s (attempt {2}/{3})", reason, seconds, attempt + 1, MaxRequestAttempts);
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }

        private static bool IsTransientStatus(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.RequestTimeout ||
                   statusCode == HttpStatusCode.TooManyRequests ||
                   statusCode == HttpStatusCode.InternalServerError ||
                   statusCode == HttpStatusCode.BadGateway ||
                   statusCode == HttpStatusCode.ServiceUnavailable ||
                   statusCode == HttpStatusCode.GatewayTimeout;
        }

        private static bool IsTransientNetworkError(WebException exception)
        {
            return exception.Status == WebExceptionStatus.ConnectFailure ||
                   exception.Status == WebExceptionStatus.ConnectionClosed ||
                   exception.Status == WebExceptionStatus.ReceiveFailure ||
                   exception.Status == WebExceptionStatus.SendFailure ||
                   exception.Status == WebExceptionStatus.Timeout ||
                   exception.Status == WebExceptionStatus.NameResolutionFailure;
        }

        private static List<JObject> SelectAuthorContributions(JArray contributions)
        {
            var candidates = (contributions ?? new JArray())
                .OfType<JObject>()
                .Where(x => x["author"] is JObject)
                .ToList();

            var primary = candidates.Where(x => IsPrimaryAuthorRole(GetString(x["contribution"]))).ToList();
            var fallback = candidates.Where(x => !NonPrimaryRoles.Contains(GetString(x["contribution"]))).ToList();
            var selected = primary.Any() ? primary : (fallback.Any() ? fallback : candidates);

            return selected
                .GroupBy(x => GetInt(AsObject(x["author"])?["id"]))
                .Where(x => x.Key != 0)
                .Select(x => x.First())
                .ToList();
        }

        private static List<JObject> SelectAuthors(JArray contributions)
        {
            return SelectAuthorContributions(contributions)
                .Select(x => x["author"] as JObject)
                .Where(x => x != null)
                .ToList();
        }

        private static bool IsPrimaryAuthorRole(string role)
        {
            return role.IsNullOrWhiteSpace() ||
                   role.Equals("author", StringComparison.OrdinalIgnoreCase) ||
                   role.Equals("author/narrator", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<int> GetAuthorIds(WorkResource work)
        {
            return (work?.Authors ?? new List<AuthorResource>())
                .Select(x => x.ForeignId)
                .Concat((work?.Books ?? new List<BookResource>())
                    .SelectMany(x => x.Contributors ?? new List<ContributorResource>())
                    .Select(x => x.ForeignId))
                .Where(x => x != 0)
                .Distinct();
        }

        private static object GetCacheLock(string key)
        {
            var hash = StringComparer.Ordinal.GetHashCode(key) & int.MaxValue;
            return CacheLocks[hash % CacheLocks.Length];
        }

        private static T GetCached<T>(ICached<T> cache, string key, Func<T> factory, TimeSpan lifetime)
        {
            lock (GetCacheLock(key))
            {
                return cache.Get(key, factory, lifetime);
            }
        }

        private static bool LooksLikeIsbnOrAsin(string value)
        {
            var normalized = value.Replace("-", string.Empty).Replace(" ", string.Empty);
            return AsinRegex.IsMatch(normalized) || Isbn10Regex.IsMatch(normalized) || Isbn13Regex.IsMatch(normalized);
        }

        private static int ParseId(string value, string type)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                throw new BadRequestException($"Invalid Hardcover {type} id: {value}");
            }

            return id;
        }

        // A JSON null deserialises to a JValue whose Type is Null -- NOT a C#
        // null -- so `token?["child"]` does not short-circuit and instead throws
        // "Cannot access child value on Newtonsoft.Json.Linq.JValue". Normalise
        // JSON nulls to real nulls before indexing into them.
        private static JToken AsObject(JToken token)
        {
            return token == null || token.Type == JTokenType.Null ? null : token;
        }

        private static string GetString(JToken token)
        {
            return token?.Type == JTokenType.Null ? null : token?.Value<string>();
        }

        private static int GetInt(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return 0;
            }

            return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private static int? GetNullableInt(JToken token)
        {
            var value = GetInt(token);
            return value == 0 ? null : value;
        }

        private static double GetDouble(JToken token)
        {
            return double.TryParse(token?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private static List<string> GetGenres(JToken token)
        {
            var values = new List<string>();

            if (token == null || token.Type == JTokenType.Null)
            {
                return new List<string> { "none" };
            }

            if (token.Type == JTokenType.String)
            {
                var raw = token.Value<string>();
                if (!raw.IsNullOrWhiteSpace())
                {
                    try
                    {
                        token = JToken.Parse(raw);
                    }
                    catch (JsonException)
                    {
                        values.Add(raw);
                    }
                }
            }

            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    if (item.Type == JTokenType.String)
                    {
                        values.Add(item.Value<string>());
                    }
                    else if (item is JObject itemObject)
                    {
                        values.Add(GetString(itemObject["tag"]));
                        values.Add(GetString(itemObject["name"]));
                    }
                }
            }
            else if (token.Type == JTokenType.Object)
            {
                values.Add(GetString(token["tag"]));
                values.Add(GetString(token["name"]));
                values.AddRange(token.Values<string>());
            }

            return values.Where(x => !x.IsNullOrWhiteSpace()).Distinct(StringComparer.InvariantCultureIgnoreCase).ToList() is { Count: > 0 } genres
                ? genres
                : new List<string> { "none" };
        }

        private static DateTime? ParseDate(string value)
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            {
                return date;
            }

            return null;
        }

        private static string CleanSubtitle(string title, string subtitle)
        {
            if (title.IsNullOrWhiteSpace() || subtitle.IsNullOrWhiteSpace())
            {
                return title;
            }

            var suffix = $": {subtitle}";
            return title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? title.Substring(0, title.Length - suffix.Length) : title;
        }

        private static string HardcoverUrl(string resource, string slug, int id)
        {
            return $"https://hardcover.app/{resource}/{(slug.IsNullOrWhiteSpace() ? id.ToString(CultureInfo.InvariantCulture) : slug)}";
        }

        private static string GetApiUrl()
        {
            return WithFallback(Environment.GetEnvironmentVariable("HARDCOVER_API_URL"), DefaultApiUrl);
        }

        private static string GetApiToken()
        {
            var value = WithFallback(Environment.GetEnvironmentVariable("HARDCOVER_AUTH"), Environment.GetEnvironmentVariable("HARDCOVER_API_KEY"));
            if (value.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            const string bearerPrefix = "Bearer ";
            return value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) ? value.Substring(bearerPrefix.Length).Trim() : value.Trim();
        }

        private static string WithFallback(string value, string fallback)
        {
            return value.IsNullOrWhiteSpace() ? fallback : value;
        }
    }
}
