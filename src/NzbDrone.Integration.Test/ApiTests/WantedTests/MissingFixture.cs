using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using Readarr.Api.V1.RootFolders;

namespace NzbDrone.Integration.Test.ApiTests.WantedTests
{
    [TestFixture]
    public class MissingFixture : IntegrationTest
    {
        [SetUp]
        public void Setup()
        {
            // Add a root folder
            RootFolders.Post(new RootFolderResource
            {
                Name = "TestLibrary",
                Path = AuthorRootFolder,
                DefaultMetadataProfileId = 1,
                DefaultQualityProfileId = 1,
                DefaultMonitorOption = MonitorTypes.All
            });
        }

        [Test]
        public void missing_should_be_empty()
        {
            EnsureNoAuthor("14586394", "Andrew Hunter Murray");

            var result = WantedMissing.GetPaged(0, 15, "releaseDate", "desc");

            result.Records.Should().BeEmpty();
        }

        [Test]
        [DependsOnTest(nameof(missing_should_be_empty), AllowFailure = true)]
        public void missing_should_have_monitored_items()
        {
            EnsureAuthor("14586394", "43765115", "Andrew Hunter Murray", true);

            var result = WantedMissing.GetPaged(0, 15, "releaseDate", "desc");

            result.Records.Should().NotBeEmpty();
        }

        [Test]
        [DependsOnTest(nameof(missing_should_have_monitored_items), AllowFailure = true)]
        public void missing_should_have_author()
        {
            EnsureAuthor("14586394", "43765115", "Andrew Hunter Murray", true);

            var result = WantedMissing.GetPagedIncludeAuthor(0, 15, "releaseDate", "desc", includeAuthor: true);

            result.Records.First().Author.Should().NotBeNull();
            result.Records.First().Author.AuthorName.Should().Be("Andrew Hunter Murray");
        }

        [Test]
        [DependsOnTest(nameof(missing_should_have_author), AllowFailure = true)]
        public void missing_should_not_have_author()
        {
            EnsureAuthor("14586394", "43765115", "Andrew Hunter Murray", true);

            var result = WantedMissing.GetPagedIncludeAuthor(0, 15, "releaseDate", "desc", includeAuthor: false);

            result.Records.First().Author.Should().BeNull();
        }

        [Test]
        [DependsOnTest(nameof(missing_should_not_have_author), AllowFailure = true)]
        public void missing_should_not_have_unmonitored_items()
        {
            EnsureAuthor("14586394", "43765115", "Andrew Hunter Murray", false);

            var result = WantedMissing.GetPaged(0, 15, "releaseDate", "desc");

            result.Records.Should().BeEmpty();
        }

        [Test]
        [DependsOnTest(nameof(missing_should_not_have_unmonitored_items), AllowFailure = true)]
        public void missing_should_have_unmonitored_items()
        {
            EnsureAuthor("14586394", "43765115", "Andrew Hunter Murray", false);

            var result = WantedMissing.GetPaged(0, 15, "releaseDate", "desc", "monitored", false);

            result.Records.Should().NotBeEmpty();
        }
    }
}
