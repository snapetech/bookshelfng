using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Notifications.BookLore;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.BookLore
{
    [TestFixture]
    public class BookLoreProxyFixture : CoreTest<BookLoreProxy>
    {
        [Test]
        public void should_not_follow_redirects_when_authenticating()
        {
            HttpRequest loginRequest = null;
            Mocker.GetMock<IHttpClient>()
                .Setup(client => client.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(request =>
                {
                    loginRequest = request;
                    return new HttpResponse(
                        request,
                        new HttpHeader { ContentType = "application/json" },
                        "{\"accessToken\":\"test-token\"}");
                });

            Subject.Test(new BookLoreSettings
            {
                BaseUrl = "http://booklore.local:6060",
                Username = "reader",
                Password = "secret",
            }).Should().BeNull();

            loginRequest.Should().NotBeNull();
            loginRequest.AllowAutoRedirect.Should().BeFalse();
        }
    }
}
