using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Xml;
using System.Xml.Linq;
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
    /// Additional Google Books, Library of Congress, Europeana, Gutendex,
    /// Internet Archive, and Apify catalogs.
    /// Provider-qualified foreign IDs are retained in Bookshelf so subsequent
    /// book and author refreshes resolve through the same catalog.
    /// </summary>
    public class AdditionalBookMetadataProxy : IAdditionalBookMetadataProxy
    {
        private const string GoogleBooks = "googlebooks";
        private const string LibraryOfCongress = "loc";
        private const string Europeana = "europeana";
        private const string Gutendex = "gutendex";
        private const string InternetArchive = "internetarchive";
        private const string NdlSearch = "ndl";
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
            _searchCache = cacheManager.GetCache<List<Book>>(GetType(), "search");
            _apifyCache = cacheManager.GetCache<List<JObject>>(GetType(), "apify");
            _logger = logger;
        }

        private static HashSet<string> EnabledSources => AdditionalMetadataSources.GetEnabledSources(
            Environment.GetEnvironmentVariable("BOOKSHELF_METADATA_SOURCES"),
            Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY"),
            Environment.GetEnvironmentVariable("EUROPEANA_API_KEY"));

        private static bool IsEnabled(string provider) => EnabledSources.Contains(provider);

        public List<Book> Search(string query)
        {
            var cacheKey = "AdditionalMetadata:" + string.Join(",", EnabledSources.OrderBy(x => x)) + ":" + query.Trim().ToLowerInvariant();
            return _searchCache.Get(cacheKey, () => SearchUncached(query), TimeSpan.FromMinutes(10));
        }

        private List<Book> SearchUncached(string query)
        {
            var books = new List<Book>();
            foreach (var provider in EnabledSources
                .OrderBy(x => x == GoogleBooks ? 0 : x == Gutendex ? 1 : x == InternetArchive ? 2 : x == Europeana ? 3 : x == LibraryOfCongress ? 4 : x == NdlSearch ? 5 : x == ApifyGoodreads ? 6 : 7)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase))
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
                    else if (provider == Europeana)
                    {
                        books.AddRange(SearchEuropeana(query));
                    }
                    else if (provider == Gutendex)
                    {
                        books.AddRange(SearchGutendex(query));
                    }
                    else if (provider == InternetArchive)
                    {
                        books.AddRange(SearchInternetArchive(query));
                    }
                    else if (provider == NdlSearch)
                    {
                        books.AddRange(SearchNdl(query));
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
            HasPrefix(foreignBookId, Europeana) ||
            HasPrefix(foreignBookId, Gutendex) ||
            HasPrefix(foreignBookId, InternetArchive) ||
            HasPrefix(foreignBookId, NdlSearch) ||
            HasPrefix(foreignBookId, ApifyGoodreads);

        public bool HandlesAuthorId(string foreignAuthorId) =>
            HasPrefix(foreignAuthorId, GoogleBooks + "-author") ||
            HasPrefix(foreignAuthorId, LibraryOfCongress + "-author") ||
            HasPrefix(foreignAuthorId, Europeana + "-author") ||
            HasPrefix(foreignAuthorId, Gutendex + "-author") ||
            HasPrefix(foreignAuthorId, InternetArchive + "-author") ||
            HasPrefix(foreignAuthorId, NdlSearch + "-author") ||
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
            else if (provider == Europeana)
            {
                var key = Environment.GetEnvironmentVariable("EUROPEANA_API_KEY");
                if (key.IsNullOrWhiteSpace())
                {
                    throw new BookInfoException("Europeana requires EUROPEANA_API_KEY.");
                }

                var recordId = Decode(id).Trim('/');
                var segments = recordId.Split('/');
                if (segments.Length != 2 || segments.Any(x => x.IsNullOrWhiteSpace()))
                {
                    throw new BookInfoException("Europeana returned an invalid record identifier.");
                }

                var escapedId = string.Join("/", segments.Select(Uri.EscapeDataString));
                var url = new HttpRequestBuilder($"https://api.europeana.eu/record/v2/{escapedId}.json")
                    .AddQueryParam("wskey", key)
                    .Build();
                record = _cachedHttpClient.Get<JObject>(url, true, TimeSpan.FromDays(1)).Resource;
            }
            else if (provider == Gutendex)
            {
                if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var bookId) || bookId <= 0)
                {
                    throw new BookNotFoundException(foreignBookId);
                }

                var url = new HttpRequestBuilder("https://gutendex.com/books/{id}/")
                    .SetSegment("id", bookId.ToString(CultureInfo.InvariantCulture))
                    .Build();
                record = _cachedHttpClient.Get<JObject>(url, true, TimeSpan.FromDays(1)).Resource;
            }
            else if (provider == InternetArchive)
            {
                var identifier = SafeInternetArchiveIdentifier(Decode(id));
                var url = new HttpRequestBuilder("https://archive.org/metadata/{identifier}")
                    .SetSegment("identifier", identifier)
                    .Build();
                var response = _cachedHttpClient.Get<JObject>(url, true, TimeSpan.FromDays(1)).Resource;
                record = response["metadata"] as JObject ?? throw new BookNotFoundException(foreignBookId);
            }
            else if (provider == NdlSearch)
            {
                var recordId = SafeNdlRecordId(Decode(id));
                var request = new HttpRequestBuilder("https://ndlsearch.ndl.go.jp/api/sru")
                    .AddQueryParam("operation", "searchRetrieve")
                    .AddQueryParam("version", "1.2")
                    .AddQueryParam("maximumRecords", "1")
                    .AddQueryParam("recordSchema", "dcndl")
                    .AddQueryParam("query", "itemno=" + recordId)
                    .WithRateLimit(1)
                    .Build();
                var response = _cachedHttpClient.Get(request, true, TimeSpan.FromDays(1));
                record = ParseNdlDetail(response.Content, recordId);
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
                provider == LibraryOfCongress ? MapLocRecord(record) :
                provider == Europeana ? MapEuropeanaRecord(record) :
                provider == Gutendex ? MapGutendexRecord(record) :
                provider == InternetArchive ? MapInternetArchiveRecord(record) :
                provider == NdlSearch ? MapNdlRecord(record) : MapApifyRecord(record);
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

        private List<Book> SearchEuropeana(string query)
        {
            var key = Environment.GetEnvironmentVariable("EUROPEANA_API_KEY");
            if (key.IsNullOrWhiteSpace())
            {
                return new List<Book>();
            }

            var request = new HttpRequestBuilder("https://api.europeana.eu/record/v2/search.json")
                .AddQueryParam("query", query)
                .AddQueryParam("qf", "TYPE:TEXT")
                .AddQueryParam("reusability", "open")
                .AddQueryParam("rows", "10")
                .AddQueryParam("profile", "rich")
                .AddQueryParam("wskey", key)
                .Build();
            var response = _cachedHttpClient.Get<JObject>(request, true, TimeSpan.FromDays(1)).Resource;
            return (response["items"] as JArray ?? new JArray())
                .OfType<JObject>().Select(MapEuropeanaRecord).Where(x => x != null).ToList();
        }

        private List<Book> SearchGutendex(string query)
        {
            var request = new HttpRequestBuilder("https://gutendex.com/books/")
                .AddQueryParam("search", query)
                .AddQueryParam("page", "1")
                .Build();
            var response = _cachedHttpClient.Get<JObject>(request, true, TimeSpan.FromDays(1)).Resource;
            return (response["results"] as JArray ?? new JArray())
                .OfType<JObject>().Select(MapGutendexRecord).Where(x => x != null).Take(10).ToList();
        }

        private List<Book> SearchInternetArchive(string query)
        {
            var terms = query.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Take(20)
                .Select(EscapeLuceneTerm)
                .Where(x => !x.IsNullOrWhiteSpace())
                .Select(x => $"(title:\"{x}\" OR creator:\"{x}\")")
                .ToList();
            if (terms.Count == 0)
            {
                return new List<Book>();
            }

            var request = new HttpRequestBuilder("https://archive.org/advancedsearch.php")
                .AddQueryParam("q", "mediatype:texts AND " + string.Join(" AND ", terms))
                .AddQueryParam("fl[]", "identifier")
                .AddQueryParam("fl[]", "title")
                .AddQueryParam("fl[]", "creator")
                .AddQueryParam("fl[]", "date")
                .AddQueryParam("fl[]", "description")
                .AddQueryParam("fl[]", "language")
                .AddQueryParam("fl[]", "publisher")
                .AddQueryParam("fl[]", "isbn")
                .AddQueryParam("rows", "10")
                .AddQueryParam("page", "1")
                .AddQueryParam("output", "json")
                .Build();
            var response = _cachedHttpClient.Get<JObject>(request, true, TimeSpan.FromDays(1)).Resource;
            return (response["response"]?["docs"] as JArray ?? new JArray())
                .OfType<JObject>().Select(MapInternetArchiveRecord).Where(x => x != null).ToList();
        }

        private List<Book> SearchNdl(string query)
        {
            var request = new HttpRequestBuilder("https://ndlsearch.ndl.go.jp/api/opensearch")
                .AddQueryParam("any", query)
                .AddQueryParam("mediatype", "books")
                .AddQueryParam("cnt", "10")
                .WithRateLimit(1)
                .Build();
            var response = _cachedHttpClient.Get(request, true, TimeSpan.FromDays(1));
            var document = ParseXmlSafely(response.Content);
            return document.Descendants()
                .Where(x => x.Name.LocalName == "item")
                .Select(x => MapNdlRecord(NdlElementToRecord(x, null)))
                .Where(x => x != null)
                .ToList();
        }

        private static JObject ParseNdlDetail(string response, string recordId)
        {
            var document = ParseXmlSafely(response);
            var recordData = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "recordData");
            if (recordData == null || recordData.Value.IsNullOrWhiteSpace())
            {
                throw new BookNotFoundException(recordId);
            }

            var recordDocument = ParseXmlSafely(recordData.Value);
            var bibliography = recordDocument.Descendants()
                .FirstOrDefault(x => x.Name.LocalName == "BibResource") ?? recordDocument.Root;
            return bibliography == null ? throw new BookNotFoundException(recordId) : NdlElementToRecord(bibliography, recordId);
        }

        private static XDocument ParseXmlSafely(string xml)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new System.IO.StringReader(xml ?? string.Empty), settings);
            return XDocument.Load(reader, LoadOptions.None);
        }

        private static JObject NdlElementToRecord(XElement element, string recordId)
        {
            var idUrl = recordId ?? FindNdlText(element, "link") ?? FindNdlText(element, "guid");
            var id = SafeNdlRecordId(idUrl);
            var title = FindNdlText(element, "title");
            var creators = FindNdlTexts(element, "creator");
            var identifiers = element.DescendantsAndSelf()
                .Where(x => x.Name.LocalName == "identifier")
                .Where(x => ((string)x.Attribute(XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance")) is string type && type.IndexOf("ISBN", StringComparison.OrdinalIgnoreCase) >= 0) ||
                    System.Text.RegularExpressions.Regex.IsMatch(x.Value.Trim(), "^(?:97[89][0-9]{10}|[0-9]{9}[0-9Xx])$"))
                .Select(x => NormalizeEuropeanaIsbn13(x.Value))
                .Where(x => x != null)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return new JObject
            {
                ["identifier"] = id,
                ["title"] = title,
                ["creator"] = new JArray(creators),
                ["publisher"] = FindNdlText(element, "publisher"),
                ["language"] = FindNdlText(element, "language"),
                ["date"] = FindNdlText(element, "issued") ?? FindNdlText(element, "date"),
                ["description"] = FindNdlText(element, "abstract") ?? FindNdlText(element, "description"),
                ["extent"] = FindNdlText(element, "extent"),
                ["isbn"] = new JArray(identifiers)
            };
        }

        private static string FindNdlText(XElement element, string localName) =>
            element.DescendantsAndSelf()
                .Where(x => x.Name.LocalName == localName && !x.HasElements)
                .Select(x => x.Value.Trim())
                .FirstOrDefault(x => !x.IsNullOrWhiteSpace());

        private static List<string> FindNdlTexts(XElement element, string localName) =>
            element.DescendantsAndSelf()
                .Where(x => x.Name.LocalName == localName && !x.HasElements)
                .Select(x => x.Value.Trim())
                .Where(x => !x.IsNullOrWhiteSpace())
                .Distinct(StringComparer.Ordinal)
                .ToList();

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

            var template = Environment.GetEnvironmentVariable("HARDCOVER_APIFY_GOODREADS_INPUT_TEMPLATE");
            if (template.IsNullOrWhiteSpace())
            {
                template = "{\"searchQueries\":[{{query}}],\"maxItems\":10}";
            }

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
            var language = record["language"] is JArray languages
                ? languages.Values<string>().FirstOrDefault()
                : (string)record["language"];
            return BuildBook(
                LibraryOfCongress,
                Encode(uri.ToString()),
                (string)record["title"],
                author,
                (record["description"] as JArray)?.Values<string>().FirstOrDefault() ?? (string)record["description"],
                (record["publisher"] as JArray)?.Values<string>().FirstOrDefault(),
                language,
                (string)record["date"],
                null,
                (string)record["image_url"],
                record["identifiers"] as JArray,
                uri.ToString());
        }

        private Book MapGutendexRecord(JObject record)
        {
            if (record == null || !int.TryParse((string)record["id"], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                return null;
            }

            var author = (record["authors"] as JArray)?.OfType<JObject>()
                .Select(x => (string)x["name"]).FirstOrDefault(x => !x.IsNullOrWhiteSpace());
            var identifiers = new JArray();
            var image = (string)record["formats"]?["image/jpeg"] ?? (string)record["formats"]?["image/png"];
            var summaries = record["summaries"] as JArray;
            var idText = id.ToString(CultureInfo.InvariantCulture);
            return BuildBook(
                Gutendex,
                idText,
                (string)record["title"],
                author,
                summaries?.Values<string>().FirstOrDefault(),
                null,
                GetFirstString(record["languages"]),
                null,
                null,
                image,
                identifiers,
                "https://www.gutenberg.org/ebooks/" + idText);
        }

        private Book MapInternetArchiveRecord(JObject record)
        {
            if (record == null)
            {
                return null;
            }

            string identifier;
            try
            {
                identifier = SafeInternetArchiveIdentifier((string)record["identifier"]);
            }
            catch (BookInfoException)
            {
                return null;
            }

            var title = GetFirstString(record["title"]);
            var author = GetFirstString(record["creator"]);
            var isbnValues = GetStringValues(record["isbn"])
                .Select(NormalizeEuropeanaIsbn13)
                .Where(x => x != null)
                .Distinct(StringComparer.Ordinal);
            var identifiers = new JArray(isbnValues);
            var pages = (string)record["numberofpages"] ?? (string)record["page_count"];
            var pageCount = int.TryParse(pages, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPages)
                ? parsedPages
                : (int?)null;
            var image = "https://archive.org/services/img/" + Uri.EscapeDataString(identifier);

            return BuildBook(
                InternetArchive,
                Encode(identifier),
                title,
                author,
                GetFirstString(record["description"]),
                GetFirstString(record["publisher"]),
                GetFirstString(record["language"]),
                (string)record["date"],
                pageCount,
                image,
                identifiers,
                "https://archive.org/details/" + Uri.EscapeDataString(identifier));
        }

        private Book MapNdlRecord(JObject record)
        {
            if (record == null)
            {
                return null;
            }

            string recordId;
            try
            {
                recordId = SafeNdlRecordId((string)record["identifier"]);
            }
            catch (BookInfoException)
            {
                return null;
            }

            var isbnIdentifiers = new JArray(GetStringValues(record["isbn"])
                .Select(NormalizeEuropeanaIsbn13)
                .Where(x => x != null)
                .Distinct(StringComparer.Ordinal));
            var extent = (string)record["extent"];
            var pages = System.Text.RegularExpressions.Regex.Match(extent ?? string.Empty, "([0-9]+)\\s*p", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var pageCount = pages.Success && int.TryParse(pages.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                ? count
                : (int?)null;

            return BuildBook(
                NdlSearch,
                Encode(recordId),
                GetFirstString(record["title"]),
                GetFirstString(record["creator"]),
                GetFirstString(record["description"]),
                GetFirstString(record["publisher"]),
                GetFirstString(record["language"]),
                (string)record["date"],
                pageCount,
                null,
                isbnIdentifiers,
                "https://ndlsearch.ndl.go.jp/books/" + Uri.EscapeDataString(recordId));
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

        private Book MapEuropeanaRecord(JObject record)
        {
            if (record == null)
            {
                return null;
            }

            var item = record["object"] as JObject ?? record;
            var proxies = item["proxies"] as JArray;
            var proxy = proxies?.OfType<JObject>().FirstOrDefault(x => (bool?)x["europeanaProxy"] == false)
                ?? proxies?.OfType<JObject>().FirstOrDefault();
            var aggregation = (item["aggregations"] as JArray)?.OfType<JObject>().FirstOrDefault();
            var recordId = (string)item["about"] ?? (string)record["id"] ?? (string)record["about"];
            if (recordId?.StartsWith("http://data.europeana.eu/item/", StringComparison.OrdinalIgnoreCase) == true ||
                recordId?.StartsWith("https://data.europeana.eu/item/", StringComparison.OrdinalIgnoreCase) == true)
            {
                recordId = new Uri(recordId).AbsolutePath;
            }

            var recordType = (string)item["type"] ?? (string)record["type"];
            if (!recordType.IsNullOrWhiteSpace() && !recordType.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var title = GetFirstString(item["title"]) ?? GetFirstString(proxy?["dcTitle"]);
            var author = GetFirstString(item["dcCreator"]) ?? GetFirstString(proxy?["dcCreator"])
                ?? GetFirstString(item["dcContributor"]) ?? GetFirstString(proxy?["dcContributor"]);
            var description = GetFirstString(item["dcDescription"]) ?? GetFirstString(proxy?["dcDescription"]);
            var publisher = GetFirstString(item["dcPublisher"]) ?? GetFirstString(proxy?["dcPublisher"]);
            var language = GetFirstString(item["language"]) ?? GetFirstString(proxy?["dcLanguage"]);
            var date = GetFirstString(item["year"]) ?? GetFirstString(proxy?["dcDate"]);
            var image = GetFirstString(item["edmPreview"]) ?? GetFirstString(aggregation?["edmPreview"]);
            var identifiers = new JArray(GetStringValues(item["dcIdentifier"] ?? proxy?["dcIdentifier"])
                .Select(NormalizeEuropeanaIsbn13)
                .Where(x => x != null)
                .Distinct(StringComparer.Ordinal));
            var url = recordId.IsNullOrWhiteSpace()
                ? "https://www.europeana.eu/"
                : "https://www.europeana.eu/item/" + recordId.Trim('/');

            return BuildBook(
                Europeana,
                Encode(recordId ?? string.Empty),
                title,
                author,
                description,
                publisher,
                language,
                date,
                null,
                image,
                identifiers,
                url);
        }

        private static string GetFirstString(JToken value) =>
            value is JArray array ? array.Values<string>().FirstOrDefault(x => !x.IsNullOrWhiteSpace()) : value?.ToString();

        private static IEnumerable<string> GetStringValues(JToken value) =>
            value is JArray array ? array.Values<string>() : value == null ? Enumerable.Empty<string>() : new[] { value.ToString() };

        private static string NormalizeEuropeanaIsbn13(string value)
        {
            var digits = System.Text.RegularExpressions.Regex.Replace(value ?? string.Empty, "[^0-9]", string.Empty);
            return Isbn13IsValid(digits) ? digits : null;
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
            book.Links.Add(new Links { Name = provider == NdlSearch ? "NDL Search API" : provider, Url = url });
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
                provider == Europeana ? host == "europeana.eu" || host.EndsWith(".europeana.eu") :
                provider == InternetArchive ? host == "archive.org" || host.EndsWith(".archive.org") :
                provider == Gutendex ? host == "gutenberg.org" || host.EndsWith(".gutenberg.org") :
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
        private static string EscapeLuceneTerm(string value) =>
            System.Text.RegularExpressions.Regex.Replace(value ?? string.Empty, "([+\\-!(){}\\[\\]^\\\"~*?:\\\\/])", "\\\\$1");

        private static string SafeInternetArchiveIdentifier(string value)
        {
            if (value.IsNullOrWhiteSpace() || value == "." || value == ".." ||
                !System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$"))
            {
                throw new BookInfoException("Internet Archive returned an invalid item identifier.");
            }

            return value;
        }

        private static string SafeNdlRecordId(string value)
        {
            var recordId = value;
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("ndlsearch.ndl.go.jp", StringComparison.OrdinalIgnoreCase))
                {
                    throw new BookInfoException("NDL Search returned an invalid record URL.");
                }

                var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 2 && segments[0].Equals("books", StringComparison.OrdinalIgnoreCase))
                {
                    recordId = segments[1];
                }
            }

            if (recordId.IsNullOrWhiteSpace() ||
                !System.Text.RegularExpressions.Regex.IsMatch(recordId, "^R[0-9]{9}-[A-Za-z0-9-]+$"))
            {
                throw new BookInfoException("NDL Search returned an invalid bibliographic identifier.");
            }

            return recordId;
        }

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
