using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class ApiKeyAuthenticationFixture : IntegrationTest
    {
        [Test]
        public async Task should_accept_legacy_query_string_api_key()
        {
            using var client = new HttpClient();
            var url = $"{RootUrl}api/v1/qualityprofile?apikey={Uri.EscapeDataString(ApiKey)}";

            using var response = await client.GetAsync(url);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
