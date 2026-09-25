using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Authentication;

namespace NzbDrone.Integration.Test
{
    [TestFixture]
    public class IndexHtmlFixture : IntegrationTest
    {
        private HttpClient _httpClient = new HttpClient();

        [Test]
        public void should_get_index_html()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, RootUrl);
            var response = _httpClient.Send(request);
            var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            text.Should().NotBeNullOrWhiteSpace();
        }

        [Test]
        public void index_should_not_be_cached()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, RootUrl);
            var response = _httpClient.Send(request);

            var headers = response.Headers;

            headers.CacheControl.NoStore.Should().BeTrue();
            headers.CacheControl.NoCache.Should().BeTrue();
            headers.Pragma.Should().Contain(new NameValueHeaderValue("no-cache"));

            response.Content.Headers.Expires.Should().BeBefore(DateTime.UtcNow);
        }

        [Test]
        public void should_serve_ui_without_auth_and_redirect_forms_auth_without_changing_api_auth()
        {
            var config = HostConfig.Get(1);
            var originalAuthenticationMethod = config.AuthenticationMethod;
            var originalAuthenticationRequired = config.AuthenticationRequired;
            var originalUsername = config.Username;
            var originalPassword = config.Password;
            var originalPasswordConfirmation = config.PasswordConfirmation;

            config.AuthenticationRequired = AuthenticationRequiredType.Enabled;
            config.Username = "integration-user";
            config.Password = "integration-password";
            config.PasswordConfirmation = config.Password;

            try
            {
                config.AuthenticationMethod = AuthenticationType.None;
                HostConfig.Put(config);

                using var handler = new HttpClientHandler { AllowAutoRedirect = false };
                using var client = new HttpClient(handler);

                using var freshInstallUiResponse = client.GetAsync(RootUrl).GetAwaiter().GetResult();
                freshInstallUiResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                using var freshInstallApiResponse = client.GetAsync(new Uri(new Uri(RootUrl), "api/v1/system/status")).GetAwaiter().GetResult();
                freshInstallApiResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                freshInstallApiResponse.Headers.Location.Should().BeNull();

                config.AuthenticationMethod = AuthenticationType.Forms;
                HostConfig.Put(config);

                using var uiResponse = client.GetAsync(RootUrl).GetAwaiter().GetResult();
                uiResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
                uiResponse.Headers.Location.AbsolutePath.Should().Be("/login");
                uiResponse.Headers.Location.Query.Should().Contain("returnUrl=%2F");

                using var apiResponse = client.GetAsync(new Uri(new Uri(RootUrl), "api/v1/system/status")).GetAwaiter().GetResult();
                apiResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                apiResponse.Headers.Location.Should().BeNull();
            }
            finally
            {
                config.AuthenticationMethod = originalAuthenticationMethod;
                config.AuthenticationRequired = originalAuthenticationRequired;
                config.Username = originalUsername;
                config.Password = originalPassword;
                config.PasswordConfirmation = originalPasswordConfirmation;
                HostConfig.Put(config);
            }
        }
    }
}
