using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class SeriesBookLinkExtensionsFixture : TestBase
    {
        private static SeriesBookLink GivenLink(string title, int position, bool isPrimary = false)
        {
            return new SeriesBookLink
            {
                SeriesPosition = position,
                IsPrimary = isPrimary,
                Series = new LazyLoaded<Series>(new Series { Title = title })
            };
        }

        [Test]
        public void should_prefer_configured_series_names_in_their_listed_order()
        {
            var links = new List<SeriesBookLink>
            {
                GivenLink("Wheel of Time", 1, isPrimary: true),
                GivenLink("The Expanse", 3)
            };

            var preferred = links.GetPreferredSeriesLink("The Expanse\nWheel of Time");

            preferred.Series.Value.Title.Should().Be("The Expanse");
        }

        [Test]
        public void should_prefer_primary_series_then_lowest_numbered_position_by_default()
        {
            var links = new List<SeriesBookLink>
            {
                GivenLink("Unnumbered series", 0, isPrimary: true),
                GivenLink("Later series", 3),
                GivenLink("Earlier series", 1)
            };

            var preferred = links.GetPreferredSeriesLink();

            preferred.Series.Value.Title.Should().Be("Unnumbered series");

            links[0].IsPrimary = false;

            links.GetPreferredSeriesLink().Series.Value.Title.Should().Be("Earlier series");
        }

        [Test]
        public void should_return_null_when_no_link_has_a_series_title()
        {
            var links = new List<SeriesBookLink>
            {
                new SeriesBookLink(),
                new SeriesBookLink { Series = new LazyLoaded<Series>(new Series { Title = " " }) }
            };

            links.GetPreferredSeriesLink().Should().BeNull();
            ((IEnumerable<SeriesBookLink>)null).GetPreferredSeriesLink().Should().BeNull();
        }
    }
}
