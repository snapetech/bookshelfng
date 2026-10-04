using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Readarr.Api.V1.System;

namespace NzbDrone.Api.Test.System
{
    [TestFixture]
    public class SystemControllerFixture
    {
        [Test]
        public void should_advertise_format_specific_availability_statistics()
        {
            var controller = new SystemController(null, null, null, null, null, null, null, null, null, null, null);
            var response = JObject.FromObject(controller.GetSeerrCapabilities());

            response["contract"].Value<string>().Should().Be("seerrng-bookshelf");
            response["features"]["formatSpecificAvailabilityStatistics"].Value<bool>().Should().BeTrue();
        }
    }
}
