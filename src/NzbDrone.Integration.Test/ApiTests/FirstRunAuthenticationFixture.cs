using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Categories;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    [IntegrationTest]
    [Parallelizable(ParallelScope.Fixtures)]
    public class FirstRunAuthenticationFixture : IntegrationTestBase
    {
        private static int _nextPort = 18786;
        private NzbDroneRunner _runner;
        private int _port;

        public override string AuthorRootFolder => GetTempDirectory("AuthorRootFolder");

        protected override string RootUrl => $"http://localhost:{_port}/";

        protected override string ApiKey => _runner.ApiKey;

        protected override void StartTestTarget()
        {
            _port = Interlocked.Increment(ref _nextPort);
            _runner = new NzbDroneRunner(LogManager.GetCurrentClassLogger(), null, _port);
            _runner.Kill();
            _runner.Start(enableAuth: true, authenticationRequired: AuthenticationRequiredType.Enabled);
        }

        protected override void InitializeTestTarget()
        {
        }

        protected override void StopTestTarget()
        {
            _runner.Kill();
        }

        [Test]
        public async Task should_show_initial_setup_when_authentication_is_enabled_without_a_user()
        {
            var hostConfig = HostConfig.Get(1);
            hostConfig.AuthenticationMethod.Should().Be(AuthenticationType.None);
            hostConfig.Username.Should().BeEmpty();

            using var client = new HttpClient();
            using var response = await client.GetAsync(RootUrl);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            using var initializeResponse = await client.GetAsync($"{RootUrl}initialize.json");
            initializeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Test]
        public async Task should_allow_ui_requests_when_authentication_is_none_and_local_auth_is_disabled()
        {
            var hostConfig = HostConfig.Get(1);
            hostConfig.AuthenticationMethod = AuthenticationType.None;
            hostConfig.AuthenticationRequired = AuthenticationRequiredType.DisabledForLocalAddresses;
            HostConfig.Put(hostConfig);

            using var client = new HttpClient();
            using var response = await client.GetAsync($"{RootUrl}initialize.json");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
