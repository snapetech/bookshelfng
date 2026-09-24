using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Integration.Test.Client;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class CommandBodyBindingFixture : IntegrationTest
    {
        [Test]
        public void should_bind_refresh_monitored_downloads_command_body()
        {
            var response = Commands.Post(new SimpleCommandResource
            {
                Name = "RefreshMonitoredDownloads"
            });

            response.Id.Should().BeGreaterThan(0);
        }
    }
}
