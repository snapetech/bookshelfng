using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Test.BookTests
{
    [TestFixture]
    public class AuthorFormatProfileFixture
    {
        [Test]
        public void should_use_format_override_and_fall_back_to_author_profile()
        {
            var authorProfile = new QualityProfile { Id = 1, Name = "Author default" };
            var ebookProfile = new QualityProfile { Id = 2, Name = "Ebook override" };
            var audiobookProfile = new QualityProfile { Id = 3, Name = "Audiobook override" };
            var author = new Author
            {
                QualityProfileId = authorProfile.Id,
                QualityProfile = new LazyLoaded<QualityProfile>(authorProfile),
                EbookQualityProfileId = ebookProfile.Id,
                EbookQualityProfile = new LazyLoaded<QualityProfile>(ebookProfile),
                AudiobookQualityProfileId = audiobookProfile.Id,
                AudiobookQualityProfile = new LazyLoaded<QualityProfile>(audiobookProfile)
            };

            author.GetQualityProfileFor(Quality.EPUB).Should().BeSameAs(ebookProfile);
            author.GetQualityProfileFor(Quality.MP3).Should().BeSameAs(audiobookProfile);

            author.AudiobookQualityProfileId = null;
            author.GetQualityProfileFor(Quality.MP3).Should().BeSameAs(authorProfile);
        }

        [Test]
        public void should_select_metadata_overrides_by_edition_format()
        {
            var authorProfile = new MetadataProfile { Id = 1, Name = "Author default" };
            var ebookProfile = new MetadataProfile { Id = 2, Name = "Ebook override" };
            var audiobookProfile = new MetadataProfile { Id = 3, Name = "Audiobook override" };
            var author = new Author
            {
                MetadataProfileId = authorProfile.Id,
                MetadataProfile = new LazyLoaded<MetadataProfile>(authorProfile),
                EbookMetadataProfileId = ebookProfile.Id,
                EbookMetadataProfile = new LazyLoaded<MetadataProfile>(ebookProfile),
                AudiobookMetadataProfileId = audiobookProfile.Id,
                AudiobookMetadataProfile = new LazyLoaded<MetadataProfile>(audiobookProfile)
            };

            author.GetMetadataProfileFor(new Edition { IsEbook = true }).Should().BeSameAs(ebookProfile);
            author.GetMetadataProfileFor(new Edition { Format = "Audiobook" }).Should().BeSameAs(audiobookProfile);
            author.GetMetadataProfileFor(new Edition { Format = "Hardcover" }).Should().BeSameAs(authorProfile);
        }
    }
}
