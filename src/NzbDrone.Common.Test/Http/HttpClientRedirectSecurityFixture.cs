using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.TPL;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.Http
{
    [TestFixture]
    public class HttpClientRedirectSecurityFixture : TestBase<HttpClient>
    {
        private readonly List<HttpRequest> _requests = new ();

        [SetUp]
        public void Setup()
        {
            _requests.Clear();
            Mocker.SetConstant<ICacheManager>(Mocker.Resolve<CacheManager>());
            Mocker.SetConstant<IRateLimitService>(Mocker.Resolve<RateLimitService>());
            Mocker.SetConstant<IEnumerable<IHttpRequestInterceptor>>(new IHttpRequestInterceptor[0]);

            Mocker.GetMock<IHttpDispatcher>()
                .Setup(dispatcher => dispatcher.GetResponseAsync(It.IsAny<HttpRequest>(), It.IsAny<CookieContainer>()))
                .Returns<HttpRequest, CookieContainer>((request, _) =>
                {
                    _requests.Add(request);
                    if (_requests.Count == 1)
                    {
                        var headers = new HttpHeader
                        {
                            { "Location", "https://redirect-target.example/receive" }
                        };
                        return Task.FromResult(new HttpResponse(request, headers, new byte[0], HttpStatusCode.TemporaryRedirect));
                    }

                    return Task.FromResult(new HttpResponse(request, new HttpHeader(), new byte[0]));
                });
        }

        [Test]
        public void should_not_forward_api_key_headers_to_another_origin()
        {
            var request = CreateRequest();
            request.Headers.Add("X-Api-Key", "secret");

            Assert.ThrowsAsync<WebException>(async () => await Subject.ExecuteAsync(request));

            Assert.That(_requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void should_not_forward_credentials_to_another_origin()
        {
            var request = CreateRequest();
            request.Credentials = new NetworkCredential("reader", "secret");

            Assert.ThrowsAsync<WebException>(async () => await Subject.ExecuteAsync(request));

            Assert.That(_requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void should_not_forward_cookies_to_another_origin()
        {
            var request = CreateRequest();
            request.Cookies.Add("session", "secret");

            Assert.ThrowsAsync<WebException>(async () => await Subject.ExecuteAsync(request));

            Assert.That(_requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void should_not_forward_request_bodies_to_another_origin()
        {
            var request = CreateRequest();
            request.SetContent("{\"password\":\"secret\"}");

            Assert.ThrowsAsync<WebException>(async () => await Subject.ExecuteAsync(request));

            Assert.That(_requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task should_follow_public_cross_origin_redirects()
        {
            var response = await Subject.ExecuteAsync(CreateRequest());

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(_requests, Has.Count.EqualTo(2));
        }

        private static HttpRequest CreateRequest()
        {
            var request = new HttpRequest("https://configured-service.example/api/resource")
            {
                AllowAutoRedirect = true,
            };

            return request;
        }
    }
}
