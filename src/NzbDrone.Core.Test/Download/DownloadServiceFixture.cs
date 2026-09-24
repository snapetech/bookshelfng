using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadServiceFixture : CoreTest<DownloadService>
    {
        private RemoteBook _parseResult;
        private List<IDownloadClient> _downloadClients;
        [SetUp]
        public void Setup()
        {
            _downloadClients = new List<IDownloadClient>();

            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(v => v.GetDownloadClients(It.IsAny<bool>()))
                .Returns(_downloadClients);

            Mocker.GetMock<IProvideDownloadClient>()
                .Setup(v => v.GetDownloadClient(It.IsAny<DownloadProtocol>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<HashSet<int>>()))
                .Returns<DownloadProtocol, int, bool, HashSet<int>>((v, i, f, t) => _downloadClients.FirstOrDefault(d => d.Protocol == v));

            var episodes = Builder<Book>.CreateListOfSize(2)
                .TheFirst(1).With(s => s.Id = 12)
                .TheNext(1).With(s => s.Id = 99)
                .All().With(s => s.AuthorId = 5)
                .Build().ToList();

            var releaseInfo = Builder<ReleaseInfo>.CreateNew()
                .With(v => v.DownloadProtocol = DownloadProtocol.Usenet)
                .With(v => v.DownloadUrl = "http://test.site/download1.ext")
                .Build();

            _parseResult = Builder<RemoteBook>.CreateNew()
                   .With(c => c.Author = Builder<Author>.CreateNew().Build())
                   .With(c => c.Release = releaseInfo)
                   .With(c => c.Books = episodes)
                   .Build();
        }

        private Mock<IDownloadClient> WithUsenetClient()
        {
            var mock = new Mock<IDownloadClient>(MockBehavior.Default);
            mock.SetupGet(s => s.Definition).Returns(Builder<IndexerDefinition>.CreateNew().Build());

            _downloadClients.Add(mock.Object);

            mock.SetupGet(v => v.Protocol).Returns(DownloadProtocol.Usenet);

            return mock;
        }

        private Mock<IDownloadClient> WithTorrentClient()
        {
            var mock = new Mock<IDownloadClient>(MockBehavior.Default);
            mock.SetupGet(s => s.Definition).Returns(Builder<IndexerDefinition>.CreateNew().Build());

            _downloadClients.Add(mock.Object);

            mock.SetupGet(v => v.Protocol).Returns(DownloadProtocol.Torrent);

            return mock;
        }

        [Test]
        public async Task Download_report_should_publish_on_grab_event()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()));

            await Subject.DownloadReport(_parseResult, null);

            VerifyEventPublished<BookGrabbedEvent>();
        }

        [Test]
        public async Task Download_report_should_grab_using_client()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()));

            await Subject.DownloadReport(_parseResult, null);

            mock.Verify(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Once());
        }

        [Test]
        public void Download_report_should_not_publish_on_failed_grab_event()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Throws(new WebException());

            Assert.ThrowsAsync<WebException>(async () => await Subject.DownloadReport(_parseResult, null));

            VerifyEventNotPublished<BookGrabbedEvent>();
        }

        [Test]
        public void Download_report_should_trigger_indexer_backoff_on_indexer_error()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Callback<RemoteBook, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseDownloadException(v.Release, "Error", new WebException());
                });

            Assert.ThrowsAsync<ReleaseDownloadException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Once());
        }

        [Test]
        public void Download_report_should_trigger_indexer_backoff_on_http429_with_long_time()
        {
            var request = new HttpRequest("http://my.indexer.com");
            var response = new HttpResponse(request, new HttpHeader(), new byte[0], (HttpStatusCode)429);
            response.Headers["Retry-After"] = "300";

            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Callback<RemoteBook, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseDownloadException(v.Release, "Error", new TooManyRequestsException(request, response));
                });

            Assert.ThrowsAsync<ReleaseDownloadException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), TimeSpan.FromMinutes(5.0)), Times.Once());
        }

        [Test]
        public void Download_report_should_trigger_indexer_backoff_on_http429_based_on_date()
        {
            var request = new HttpRequest("http://my.indexer.com");
            var response = new HttpResponse(request, new HttpHeader(), new byte[0], (HttpStatusCode)429);
            response.Headers["Retry-After"] = DateTime.UtcNow.AddSeconds(300).ToString("r");

            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Callback<RemoteBook, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseDownloadException(v.Release, "Error", new TooManyRequestsException(request, response));
                });

            Assert.ThrowsAsync<ReleaseDownloadException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(),
                    It.IsInRange<TimeSpan>(TimeSpan.FromMinutes(4.9), TimeSpan.FromMinutes(5.1), Moq.Range.Inclusive)), Times.Once());
        }

        [Test]
        public void Download_report_should_not_trigger_indexer_backoff_on_downloadclient_error()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Throws(new DownloadClientException("Some Error"));

            Assert.ThrowsAsync<DownloadClientException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never());
        }

        [Test]
        public void Download_report_should_not_trigger_indexer_backoff_on_indexer_404_error()
        {
            var mock = WithUsenetClient();
            mock.Setup(s => s.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()))
                .Callback<RemoteBook, IIndexer>((v, indexer) =>
                {
                    throw new ReleaseUnavailableException(v.Release, "Error", new WebException());
                });

            Assert.ThrowsAsync<ReleaseUnavailableException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordFailure(It.IsAny<int>(), It.IsAny<TimeSpan>()), Times.Never());
        }

        [Test]
        public void should_not_attempt_download_if_client_isnt_configured()
        {
            Assert.ThrowsAsync<DownloadClientUnavailableException>(async () => await Subject.DownloadReport(_parseResult, null));

            Mocker.GetMock<IDownloadClient>().Verify(c => c.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            VerifyEventNotPublished<BookGrabbedEvent>();
        }

        [Test]
        public async Task should_attempt_download_even_if_client_is_disabled()
        {
            var mockUsenet = WithUsenetClient();

            Mocker.GetMock<IDownloadClientStatusService>()
                    .Setup(v => v.GetBlockedProviders())
                    .Returns(new List<DownloadClientStatus>
                    {
                        new DownloadClientStatus
                        {
                            ProviderId = _downloadClients.First().Definition.Id,
                            DisabledTill = DateTime.UtcNow.AddHours(3)
                        }
                    });

            await Subject.DownloadReport(_parseResult, null);

            Mocker.GetMock<IDownloadClientStatusService>().Verify(c => c.GetBlockedProviders(), Times.Never());
            mockUsenet.Verify(c => c.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Once());
            VerifyEventPublished<BookGrabbedEvent>();
        }

        [Test]
        public async Task should_send_download_to_correct_usenet_client()
        {
            var mockTorrent = WithTorrentClient();
            var mockUsenet = WithUsenetClient();

            await Subject.DownloadReport(_parseResult, null);

            mockTorrent.Verify(c => c.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            mockUsenet.Verify(c => c.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Once());
        }

        [Test]
        public async Task should_send_download_to_correct_torrent_client()
        {
            var mockTorrent = WithTorrentClient();
            var mockUsenet = WithUsenetClient();

            _parseResult.Release.DownloadProtocol = DownloadProtocol.Torrent;

            await Subject.DownloadReport(_parseResult, null);

            mockTorrent.Verify(c => c.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Once());
            mockUsenet.Verify(c => c.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
        }

        [Test]
        public void should_report_an_existing_torrent_instead_of_downloading_it_again()
        {
            var mockTorrent = WithTorrentClient();
            var infoHash = "abcdef123456";
            _parseResult.Release = Builder<TorrentInfo>.CreateNew()
                .With(v => v.DownloadProtocol = DownloadProtocol.Torrent)
                .With(v => v.InfoHash = infoHash)
                .With(v => v.IndexerId = 0)
                .Build();
            mockTorrent.Setup(v => v.GetItems())
                .Returns(new[] { new DownloadClientItem { DownloadId = infoHash.ToUpperInvariant(), Title = "Existing torrent" } });

            var exception = Assert.ThrowsAsync<ExistingTorrentFoundException>(async () => await Subject.DownloadReport(_parseResult, null));

            Assert.That(exception.DownloadTitle, Is.EqualTo("Existing torrent"));
            mockTorrent.Verify(v => v.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            VerifyEventNotPublished<BookGrabbedEvent>();
        }

        [Test]
        public async Task should_adopt_an_existing_torrent_without_downloading_it_again()
        {
            var mockTorrent = WithTorrentClient();
            var infoHash = "abcdef123456";
            _parseResult.Release = Builder<TorrentInfo>.CreateNew()
                .With(v => v.DownloadProtocol = DownloadProtocol.Torrent)
                .With(v => v.InfoHash = infoHash)
                .With(v => v.IndexerId = 0)
                .Build();
            mockTorrent.Setup(v => v.GetItems())
                .Returns(new[] { new DownloadClientItem { DownloadId = infoHash.ToUpperInvariant(), Title = "Existing torrent" } });

            await Subject.AdoptExistingTorrent(_parseResult, null);

            mockTorrent.Verify(v => v.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            Mocker.GetMock<IRateLimitService>()
                .Verify(v => v.WaitAndPulseAsync(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never());
            Mocker.GetMock<IIndexerStatusService>()
                .Verify(v => v.RecordSuccess(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<BookGrabbedEvent>(e => e.DownloadId == infoHash.ToUpperInvariant())), Times.Once());
        }

        [Test]
        public void should_not_adopt_a_torrent_that_is_no_longer_in_the_download_client()
        {
            var mockTorrent = WithTorrentClient();
            _parseResult.Release = Builder<TorrentInfo>.CreateNew()
                .With(v => v.DownloadProtocol = DownloadProtocol.Torrent)
                .With(v => v.InfoHash = "abcdef123456")
                .With(v => v.IndexerId = 0)
                .Build();
            mockTorrent.Setup(v => v.GetItems()).Returns(Array.Empty<DownloadClientItem>());

            Assert.ThrowsAsync<ExistingTorrentNotFoundException>(async () => await Subject.AdoptExistingTorrent(_parseResult, null));

            mockTorrent.Verify(v => v.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            VerifyEventNotPublished<BookGrabbedEvent>();
        }

        [Test]
        public void should_not_adopt_a_torrent_when_the_client_cannot_be_checked()
        {
            var mockTorrent = WithTorrentClient();
            _parseResult.Release = Builder<TorrentInfo>.CreateNew()
                .With(v => v.DownloadProtocol = DownloadProtocol.Torrent)
                .With(v => v.InfoHash = "abcdef123456")
                .With(v => v.IndexerId = 0)
                .Build();
            mockTorrent.Setup(v => v.GetItems()).Throws(new DownloadClientException("Client unavailable"));

            Assert.ThrowsAsync<DownloadClientException>(async () => await Subject.AdoptExistingTorrent(_parseResult, null));

            mockTorrent.Verify(v => v.Download(It.IsAny<RemoteBook>(), It.IsAny<IIndexer>()), Times.Never());
            VerifyEventNotPublished<BookGrabbedEvent>();
        }
    }
}
