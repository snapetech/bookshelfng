using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.MetadataSource.Hardcover;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Hardcover
{
    [TestFixture]
    public class HardcoverMetadataProxyFixture : CoreTest<HardcoverMetadataProxy>
    {
        [SetUp]
        public void Setup()
        {
            Environment.SetEnvironmentVariable("HARDCOVER", "true");
            Environment.SetEnvironmentVariable("HARDCOVER_NATIVE", null);
            Environment.SetEnvironmentVariable("HARDCOVER_AUTH", "Bearer test-token");
            Environment.SetEnvironmentVariable("HARDCOVER_API_KEY", null);
            Environment.SetEnvironmentVariable("HARDCOVER_API_URL", null);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("HARDCOVER", null);
            Environment.SetEnvironmentVariable("HARDCOVER_NATIVE", null);
            Environment.SetEnvironmentVariable("HARDCOVER_AUTH", null);
            Environment.SetEnvironmentVariable("HARDCOVER_API_KEY", null);
            Environment.SetEnvironmentVariable("HARDCOVER_API_URL", null);
        }

        [Test]
        public void should_require_native_flag_and_token()
        {
            Subject.IsConfigured.Should().BeTrue();
            Subject.IsNativeEnabled.Should().BeTrue();

            Environment.SetEnvironmentVariable("HARDCOVER_NATIVE", "false");
            Subject.IsNativeEnabled.Should().BeFalse();

            Environment.SetEnvironmentVariable("HARDCOVER_NATIVE", null);
            Environment.SetEnvironmentVariable("HARDCOVER_AUTH", null);
            Subject.IsConfigured.Should().BeFalse();
            Subject.IsNativeEnabled.Should().BeTrue();
        }

        [Test]
        public void should_fail_clearly_when_native_is_requested_without_token()
        {
            Environment.SetEnvironmentVariable("HARDCOVER_AUTH", null);

            Subject.IsNativeEnabled.Should().BeTrue();
            Action action = () => Subject.GetWork("999");

            action.Should().Throw<BookInfoException>()
                .WithMessage("*HARDCOVER_AUTH*");
        }

        [Test]
        public void should_search_and_map_native_work()
        {
            var requests = new List<HttpRequest>();
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    requests.Add(request);
                    var payload = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var operation = payload["operationName"].Value<string>();

                    return new HttpResponse(request,
                        new HttpHeader { { "Content-Type", "application/json" } },
                        operation == "Search" ? SearchResponse : WorkResponse);
                });

            var result = Subject.Search("Foundation");

            result.Should().ContainSingle();
            result[0].WorkId.Should().Be(101);
            result[0].BookId.Should().Be(201);
            result[0].Title.Should().Be("Foundation");
            result[0].Author.Id.Should().Be(42);
            result[0].Author.Name.Should().Be("Isaac Asimov");
            var work = Subject.GetWork("101");
            work.Genres.Should().Contain("Science Fiction");
            work.Authors.Should().HaveCount(2);
            work.Books[0].Contributors.Should().HaveCount(2);
            work.Books[0].Contributors[1].ForeignId.Should().Be(43);
            work.Books[0].Format.Should().Be("Paperback");
            requests.Should().HaveCount(2);
            requests.Should().OnlyContain(x => x.Url.FullUri == "https://api.hardcover.app/v1/graphql");
            requests.Should().OnlyContain(x => x.Headers.GetSingleValue("Authorization") == "Bearer test-token");
        }

        [Test]
        public void should_search_when_optional_edition_metadata_is_json_null()
        {
            var requests = new List<HttpRequest>();
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    requests.Add(request);
                    var payload = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var operation = payload["operationName"].Value<string>();

                    return new HttpResponse(request,
                        new HttpHeader { { "Content-Type", "application/json" } },
                        operation == "Search" ? SearchResponse : CreateSparseWorkResponse());
                });

            var result = Subject.Search("Foundation");

            result.Should().ContainSingle();
            result[0].WorkId.Should().Be(101);
            result[0].BookId.Should().Be(201);

            var work = Subject.GetWork("101");
            work.Books.Should().ContainSingle();
            work.Books[0].Language.Should().BeNull();
            work.Books[0].Publisher.Should().BeNull();
            requests.Should().HaveCount(2);
        }

        [Test]
        public void should_return_empty_results_when_search_envelope_is_json_null()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request => new HttpResponse(request,
                    new HttpHeader { { "Content-Type", "application/json" } },
                    NullSearchResponse));

            Subject.Search("Foundation").Should().BeEmpty();
        }

        [Test]
        public void should_resolve_identifier_to_native_work()
        {
            var requests = new List<HttpRequest>();
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    requests.Add(request);
                    var payload = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var operation = payload["operationName"].Value<string>();
                    var response = operation == "GetWorkByASINISBN" ? IdentifierResponse : WorkResponse;

                    return new HttpResponse(request,
                        new HttpHeader { { "Content-Type", "application/json" } },
                        response);
                });

            var result = Subject.Search("978-0553-293357");

            result.Should().ContainSingle();
            result[0].WorkId.Should().Be(101);
            result[0].BookId.Should().Be(201);
            var identifierPayload = JObject.Parse(Encoding.UTF8.GetString(requests[0].ContentData));
            identifierPayload["variables"]["identifier"].Value<string>().Should().Be("9780553293357");
        }

        [Test]
        public void should_load_author_works_in_batched_pages()
        {
            var requests = new List<HttpRequest>();
            Mocker.GetMock<IHttpClient>()
                .Setup(x => x.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    requests.Add(request);
                    var payload = JObject.Parse(Encoding.UTF8.GetString(request.ContentData));
                    var operation = payload["operationName"].Value<string>();

                    return new HttpResponse(request,
                        new HttpHeader { { "Content-Type", "application/json" } },
                        operation == "GetAuthorEditions" ? CreateAuthorResponse() : WorkResponse);
                });

            var result = Subject.GetAuthor("42");

            result.Works.Should().ContainSingle();
            result.Works[0].ForeignId.Should().Be(101);
            result.Works[0].Authors.Should().HaveCount(2);
            requests.Should().ContainSingle();
            var authorPayload = JObject.Parse(Encoding.UTF8.GetString(requests[0].ContentData));
            authorPayload["variables"]["limit"].Value<int>().Should().Be(50);
            authorPayload["variables"]["offset"].Value<int>().Should().Be(0);
        }

        private static string CreateAuthorResponse()
        {
            var work = (JObject)JObject.Parse(WorkResponse)["data"]["books_by_pk"];
            var primaryAuthor = (JObject)work["contributions"][0]["author"];
            var author = new JObject
            {
                ["id"] = 42,
                ["name"] = primaryAuthor["name"],
                ["slug"] = primaryAuthor["slug"],
                ["bio"] = primaryAuthor["bio"],
                ["cached_image"] = primaryAuthor["cached_image"],
                ["contributions"] = new JArray
                {
                    new JObject
                    {
                        ["contribution"] = "author",
                        ["author"] = primaryAuthor.DeepClone(),
                        ["book"] = work.DeepClone()
                    }
                }
            };

            return new JObject
            {
                ["data"] = new JObject { ["authors_by_pk"] = author }
            }.ToString();
        }

        private static string CreateSparseWorkResponse()
        {
            var response = JObject.Parse(WorkResponse);
            var edition = (JObject)response["data"]["books_by_pk"]["editions"][0];
            edition["language"] = JValue.CreateNull();
            edition["publisher"] = JValue.CreateNull();
            return response.ToString();
        }

        private const string SearchResponse = @"{
            ""data"": {
                ""search"": { ""ids"": [101] }
            }
        }";

        private const string NullSearchResponse = @"{
            ""data"": {
                ""search"": null
            }
        }";

        private const string IdentifierResponse = @"{
            ""data"": {
                ""editions"": [{ ""id"": 201, ""book_id"": 101 }]
            }
        }";

        private const string WorkResponse = @"{
            ""data"": {
                ""books_by_pk"": {
                    ""id"": 101,
                    ""title"": ""Foundation"",
                    ""subtitle"": ""A Novel"",
                    ""description"": ""A science fiction novel."",
                    ""release_date"": ""1951-06-01"",
                    ""cached_tags"": [{ ""tag"": ""Science Fiction"" }],
                    ""cached_image"": ""https://images.example/foundation.jpg"",
                    ""slug"": ""foundation"",
                    ""canonical_id"": null,
                    ""rating"": 4.3,
                    ""ratings_count"": 100,
                    ""contributions"": [{
                        ""contribution"": ""author"",
                        ""author"": {
                            ""id"": 42,
                            ""name"": ""Isaac Asimov"",
                            ""slug"": ""isaac-asimov"",
                            ""bio"": ""Writer."",
                            ""cached_image"": ""https://images.example/asimov.jpg""
                        }
                    }, {
                        ""contribution"": ""author"",
                        ""author"": {
                            ""id"": 43,
                            ""name"": ""Donald Kingsbury"",
                            ""slug"": ""donald-kingsbury"",
                            ""bio"": ""Writer."",
                            ""cached_image"": null
                        }
                    }],
                    ""book_series"": [],
                    ""editions"": [{
                        ""id"": 201,
                        ""title"": ""Foundation"",
                        ""subtitle"": ""A Novel"",
                        ""asin"": ""B000000001"",
                        ""isbn_13"": ""9780553293357"",
                        ""edition_format"": null,
                        ""pages"": 255,
                        ""language"": { ""code3"": ""eng"" },
                        ""publisher"": { ""name"": ""Spectra"" },
                        ""release_date"": ""1951-06-01"",
                        ""physical_format"": ""Paperback"",
                        ""physical_information"": null,
                        ""edition_information"": null
                    }]
                }
            }
        }";
    }
}
