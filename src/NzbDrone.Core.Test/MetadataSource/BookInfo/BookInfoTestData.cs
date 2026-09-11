using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.BookInfo;
using GoodreadsResources = NzbDrone.Core.MetadataSource.Goodreads;

namespace NzbDrone.Core.Test.MetadataSource.BookInfo
{
    // Metadata provider availability and rate limits must not decide whether
    // the unit suite passes. These responses cover the provider shapes used by
    // the proxy fixtures without contacting the hosted service.
    internal static class BookInfoTestData
    {
        public static HttpResponse JsonResponse(HttpRequest request, object resource, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            return new HttpResponse(request,
                new HttpHeader { { "Content-Type", "application/json" } },
                JsonConvert.SerializeObject(resource),
                statusCode);
        }

        public static HttpResponse<T> TypedJsonResponse<T>(HttpRequest request, object resource, HttpStatusCode statusCode = HttpStatusCode.OK)
            where T : new()
        {
            return new HttpResponse<T>(JsonResponse(request, resource, statusCode));
        }

        public static HttpResponse NotFoundResponse(HttpRequest request)
        {
            return new HttpResponse(request, new HttpHeader(), string.Empty, HttpStatusCode.NotFound);
        }

        public static HttpResponse RedirectResponse(HttpRequest request, string location)
        {
            return new HttpResponse(request,
                new HttpHeader { { "Location", location } },
                string.Empty,
                HttpStatusCode.MovedPermanently);
        }

        public static List<GoodreadsResources.SearchJsonResource> GoodreadsSearchResults(string query)
        {
            if (query.Equals("Robert Harris", StringComparison.OrdinalIgnoreCase))
            {
                return new List<GoodreadsResources.SearchJsonResource> { SearchResult(575, 100575, 100575, query) };
            }

            if (query.Equals("Lyndsay Ely", StringComparison.OrdinalIgnoreCase))
            {
                return new List<GoodreadsResources.SearchJsonResource> { SearchResult(8056539, 108056539, 108056539, query) };
            }

            if (query.Equals("Elisa Puricelli Guerra", StringComparison.OrdinalIgnoreCase))
            {
                return new List<GoodreadsResources.SearchJsonResource> { SearchResult(4481805, 104481805, 104481805, query) };
            }

            if (query.Equals("Harry Potter and the sorcerer's stone a detailed summary", StringComparison.OrdinalIgnoreCase))
            {
                return new List<GoodreadsResources.SearchJsonResource> { SearchResult(42, 72245296, 72245296, "Harry Potter and the Sorcerer's Stone") };
            }

            if (query.Equals("B0192CTMYG", StringComparison.OrdinalIgnoreCase))
            {
                return new List<GoodreadsResources.SearchJsonResource> { SearchResult(42, 61209488, 61209488, "Harry Potter and the Sorcerer's Stone") };
            }

            if (query.Equals("9780439554930", StringComparison.OrdinalIgnoreCase))
            {
                return new List<GoodreadsResources.SearchJsonResource> { SearchResult(42, 3, 3, "Harry Potter and the Sorcerer's Stone") };
            }

            return new List<GoodreadsResources.SearchJsonResource>();
        }

        public static List<GoodreadsResources.SearchJsonResource> BookInfoSearchResults(string query)
        {
            if (query.Equals("Robert Harris", StringComparison.OrdinalIgnoreCase))
            {
                return new List<GoodreadsResources.SearchJsonResource> { SearchResult(575, 100575, 100575, query) };
            }

            if (query.Equals("Lyndsay Ely", StringComparison.OrdinalIgnoreCase))
            {
                return new List<GoodreadsResources.SearchJsonResource> { SearchResult(8056539, 108056539, 108056539, query) };
            }

            if (query.Equals("Elisa Puricelli Guerra", StringComparison.OrdinalIgnoreCase))
            {
                return new List<GoodreadsResources.SearchJsonResource> { SearchResult(4481805, 104481805, 104481805, query) };
            }

            if (query.Equals("Roald Dahl", StringComparison.OrdinalIgnoreCase))
            {
                return new List<GoodreadsResources.SearchJsonResource>
                {
                    SearchResult(999, 9001, 9001, "Matilda"),
                    SearchResult(999, 9002, 9002, "The BFG")
                };
            }

            if (query.Equals("Harry Potter and the sorcerer's stone a summary of the novel", StringComparison.OrdinalIgnoreCase))
            {
                return new List<GoodreadsResources.SearchJsonResource> { SearchResult(42, 72245296, 61209488, "Harry Potter and the Sorcerer's Stone (Book 1)") };
            }

            if (query.Equals("9780439554930", StringComparison.OrdinalIgnoreCase))
            {
                return new List<GoodreadsResources.SearchJsonResource> { SearchResult(42, 3, 3, "Harry Potter and the Sorcerer's Stone") };
            }

            return new List<GoodreadsResources.SearchJsonResource>();
        }

        public static AuthorResource Author(int id, string name, int workId = 0)
        {
            workId = workId == 0 ? 10000000 + id : workId;

            return new AuthorResource
            {
                ForeignId = id,
                Name = name,
                Description = $"{name} test description",
                ImageUrl = $"https://example.test/authors/{id}.jpg",
                Url = $"https://example.test/authors/{id}",
                RatingCount = 100,
                AverageRating = 4.0,
                Works = new List<WorkResource> { Work(workId, $"{name} test work", id, name, workId + 1) },
                Series = new List<SeriesResource>()
            };
        }

        public static WorkResource Work(int id, string title, int authorId, string authorName, int editionId, string editionTitle = null, List<SeriesResource> series = null)
        {
            return new WorkResource
            {
                ForeignId = id,
                Title = title,
                Url = $"https://example.test/works/{id}",
                Genres = new List<string> { "Fiction" },
                RelatedWorks = new List<int>(),
                Books = new List<BookResource> { Edition(editionId, editionTitle ?? title, authorId) },
                Series = series ?? new List<SeriesResource>(),
                Authors = new List<AuthorResource> { AuthorSummary(authorId, authorName) }
            };
        }

        public static BulkBookResource CombinedSearchResponse()
        {
            return new BulkBookResource
            {
                Authors = new List<AuthorResource> { AuthorSummary(999, "Roald Dahl") },
                Series = new List<SeriesResource>(),
                Works = new List<WorkResource>
                {
                    Work(9001, "Matilda", 999, "Roald Dahl", 99001),
                    Work(9002, "The BFG", 999, "Roald Dahl", 99002)
                }
            };
        }

        public static HttpResponse DetailResponse(HttpRequest request)
        {
            var path = request.Url.Path.Trim('/');

            if (path == "work/1128601")
            {
                return JsonResponse(request, Work(1128601, "Guards! Guards!", 1654, "Terry Pratchett", 11286011));
            }

            if (path == "work/3293141")
            {
                return JsonResponse(request, Work(3293141, "The Iliad", 42, "Homer", 32931411));
            }

            if (path == "work/14190696")
            {
                return JsonResponse(request, Work(
                    14190696,
                    "The Book of Dust",
                    1654,
                    "Terry Pratchett",
                    141906961,
                    series: new List<SeriesResource>
                    {
                        Series(1, "The Book of Dust", 14190696, "1", 1)
                    }));
            }

            if (path == "work/48427681")
            {
                return JsonResponse(request, Work(
                    48427681,
                    "October Daye Chronological Order",
                    1654,
                    "Terry Pratchett",
                    484276811,
                    series: new List<SeriesResource>
                    {
                        Series(2, "October Daye Chronological Order", 48427681, "7.1", 7)
                    }));
            }

            if (path == "work/3")
            {
                return JsonResponse(request, Work(3, "Harry Potter and the Sorcerer's Stone", 42, "J. K. Rowling", 3));
            }

            if (path == "work/72245296")
            {
                return JsonResponse(request, Work(
                    72245296,
                    "Harry Potter and the Sorcerer's Stone (Book 1)",
                    42,
                    "J. K. Rowling",
                    61209488,
                    "Harry Potter and the Sorcerer's Stone (Book 1)"));
            }

            if (path.StartsWith("author/", StringComparison.Ordinal))
            {
                var id = path.Substring("author/".Length);
                if (id == "575")
                {
                    return JsonResponse(request, Author(575, "Robert Harris", 100575));
                }

                if (id == "1654")
                {
                    return JsonResponse(request, Author(1654, "Terry Pratchett", 1128601));
                }

                if (id == "8056539")
                {
                    return JsonResponse(request, Author(8056539, "Lyndsay Ely", 108056539));
                }

                if (id == "4481805")
                {
                    return JsonResponse(request, Author(4481805, "Elisa Puricelli Guerra", 104481805));
                }
            }

            return NotFoundResponse(request);
        }

        public static HttpResponse EditionLookupResponse(HttpRequest request)
        {
            var path = request.Url.Path.Trim('/');

            if (path == "book/3")
            {
                return RedirectResponse(request, "https://example.test/work/3");
            }

            if (path == "book/61209488")
            {
                return RedirectResponse(request, "https://example.test/work/72245296");
            }

            return NotFoundResponse(request);
        }

        private static GoodreadsResources.SearchJsonResource SearchResult(int authorId, int workId, int bookId, string title)
        {
            return new GoodreadsResources.SearchJsonResource
            {
                Author = new GoodreadsResources.AuthorJsonResource
                {
                    Id = authorId,
                    Name = title
                },
                WorkId = workId,
                BookId = bookId,
                Title = title,
                BookTitleBare = title
            };
        }

        private static AuthorResource AuthorSummary(int id, string name)
        {
            return new AuthorResource
            {
                ForeignId = id,
                Name = name,
                Description = $"{name} test description",
                ImageUrl = $"https://example.test/authors/{id}.jpg",
                Url = $"https://example.test/authors/{id}"
            };
        }

        private static BookResource Edition(int id, string title, int authorId)
        {
            return new BookResource
            {
                ForeignId = id,
                Title = title,
                Description = $"{title} test description",
                Isbn13 = $"978{id:D10}".Substring(0, 13),
                Language = "English",
                Format = "Hardcover",
                Publisher = "Example Publisher",
                ImageUrl = $"https://example.test/editions/{id}.jpg",
                NumPages = 100,
                RatingCount = 100,
                AverageRating = 4.0,
                Url = $"https://example.test/editions/{id}",
                Contributors = new List<ContributorResource>
                {
                    new ContributorResource { ForeignId = authorId, Role = "Author" }
                }
            };
        }

        private static SeriesResource Series(int id, string title, int workId, string position, int seriesPosition)
        {
            return new SeriesResource
            {
                ForeignId = id,
                Title = title,
                Description = $"{title} test description",
                LinkItems = new List<SeriesWorkLinkResource>
                {
                    new SeriesWorkLinkResource
                    {
                        ForeignWorkId = workId,
                        PositionInSeries = position,
                        SeriesPosition = seriesPosition,
                        Primary = true
                    }
                }
            };
        }
    }
}
