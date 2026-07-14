using System;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.QBittorrent;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.QBittorrentTests
{
    [TestFixture]
    public class QBittorrentProxyV2Fixture : TestBase<QBittorrentProxyV2>
    {
        private QBittorrentSettings _settings;

        [SetUp]
        public void Setup()
        {
            _settings = new QBittorrentSettings
            {
                Host = "127.0.0.1",
                Port = 8080,
                Username = "admin",
                Password = "password"
            };
        }

        [TestCase(HttpStatusCode.OK, "Ok.", "SID")]
        [TestCase(HttpStatusCode.NoContent, "", "QBT_SID_8080")]
        public void should_authenticate_with_supported_success_response(HttpStatusCode statusCode, string content, string cookieName)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(v => v.Execute(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(request =>
                  {
                      if (request.Url.FullUri.EndsWith("/api/v2/auth/login"))
                      {
                          var headers = new HttpHeader();
                          headers.Add("Set-Cookie", $"{cookieName}=session-token; path=/");

                          return new HttpResponse(request, headers, content, statusCode);
                      }

                      request.Cookies.Should().ContainKey(cookieName);

                      return new HttpResponse(request, new HttpHeader(), "2.15.1");
                  });

            Subject.GetApiVersion(_settings).Should().Be(new Version(2, 15, 1));
        }

        [Test]
        public void should_reject_failed_login_response()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(v => v.Execute(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(request => new HttpResponse(request, new HttpHeader(), "Fails."));

            Action action = () => Subject.GetApiVersion(_settings);

            action.Should().Throw<DownloadClientAuthenticationException>();
        }

        [Test]
        public void should_authenticate_with_api_key_without_logging_in()
        {
            _settings.ApiKey = "qbt_1234567890abcdefghijklmnopqr";
            _settings.Username = null;
            _settings.Password = null;

            Mocker.GetMock<IHttpClient>()
                  .Setup(v => v.Execute(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(request =>
                  {
                      request.Url.FullUri.Should().NotEndWith("/api/v2/auth/login");
                      request.Headers.GetSingleValue("Authorization").Should().Be($"Bearer {_settings.ApiKey}");

                      return new HttpResponse(request, new HttpHeader(), "2.15.1");
                  });

            Subject.GetApiVersion(_settings).Should().Be(new Version(2, 15, 1));
            Mocker.GetMock<IHttpClient>().Verify(v => v.Execute(It.IsAny<HttpRequest>()), Times.Once);
        }

        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.Forbidden)]
        public void should_reject_invalid_api_key(HttpStatusCode statusCode)
        {
            _settings.ApiKey = "qbt_1234567890abcdefghijklmnopqr";

            Mocker.GetMock<IHttpClient>()
                  .Setup(v => v.Execute(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(request => new HttpResponse(request, new HttpHeader(), string.Empty, statusCode));

            Action action = () => Subject.GetApiVersion(_settings);

            action.Should().Throw<DownloadClientAuthenticationException>();
            Mocker.GetMock<IHttpClient>().Verify(v => v.Execute(It.IsAny<HttpRequest>()), Times.Once);
        }

        [TestCase(HttpStatusCode.Unauthorized)]
        [TestCase(HttpStatusCode.Forbidden)]
        public void should_recognize_v2_api_when_api_key_requires_authentication(HttpStatusCode statusCode)
        {
            _settings.ApiKey = "qbt_1234567890abcdefghijklmnopqr";

            Mocker.GetMock<IHttpClient>()
                  .Setup(v => v.Execute(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(request =>
                  {
                      request.Headers.GetSingleValue("Authorization").Should().Be($"Bearer {_settings.ApiKey}");

                      return new HttpResponse(request, new HttpHeader(), string.Empty, statusCode);
                  });

            Subject.IsApiSupported(_settings).Should().BeTrue();
        }
    }
}
