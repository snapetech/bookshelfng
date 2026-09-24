using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class IgnoredDownloadServiceFixture : CoreTest<IgnoredDownloadService>
    {
        [Test]
        public void should_ignore_unmatched_download_without_a_book_or_author()
        {
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem
                {
                    Title = "Unmatched release",
                    DownloadId = "client-download-id",
                    DownloadClientInfo = new DownloadClientItemClientInfo { Name = "Test client" }
                },
                RemoteBook = new RemoteBook
                {
                    Books = new List<Book>()
                }
            };

            Subject.IgnoreDownload(trackedDownload).Should().BeTrue();

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<DownloadIgnoredEvent>(e =>
                    e.AuthorId == 0 &&
                    e.BookIds.Count == 0 &&
                    e.SourceTitle == "Unmatched release" &&
                    e.DownloadId == "client-download-id")), Times.Once());
        }

        [Test]
        public void should_ignore_download_when_remote_book_is_missing()
        {
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = new DownloadClientItem
                {
                    Title = "Unmatched release",
                    DownloadId = "client-download-id",
                    DownloadClientInfo = new DownloadClientItemClientInfo { Name = "Test client" }
                }
            };

            Subject.IgnoreDownload(trackedDownload).Should().BeTrue();

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<DownloadIgnoredEvent>(e =>
                    e.AuthorId == 0 &&
                    e.BookIds.Count == 0 &&
                    e.TrackedDownload == trackedDownload)), Times.Once());
        }
    }
}
