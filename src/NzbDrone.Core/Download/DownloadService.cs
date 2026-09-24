using System;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public interface IDownloadService
    {
        Task DownloadReport(RemoteBook remoteBook, int? downloadClientId);
        Task AdoptExistingTorrent(RemoteBook remoteBook, int? downloadClientId);
    }

    public class DownloadService : IDownloadService
    {
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IDownloadClientStatusService _downloadClientStatusService;
        private readonly IIndexerFactory _indexerFactory;
        private readonly IIndexerStatusService _indexerStatusService;
        private readonly IRateLimitService _rateLimitService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ISeedConfigProvider _seedConfigProvider;
        private readonly Logger _logger;

        public DownloadService(IProvideDownloadClient downloadClientProvider,
                               IDownloadClientStatusService downloadClientStatusService,
                               IIndexerFactory indexerFactory,
                               IIndexerStatusService indexerStatusService,
                               IRateLimitService rateLimitService,
                               IEventAggregator eventAggregator,
                               ISeedConfigProvider seedConfigProvider,
                               Logger logger)
        {
            _downloadClientProvider = downloadClientProvider;
            _downloadClientStatusService = downloadClientStatusService;
            _indexerFactory = indexerFactory;
            _indexerStatusService = indexerStatusService;
            _rateLimitService = rateLimitService;
            _eventAggregator = eventAggregator;
            _seedConfigProvider = seedConfigProvider;
            _logger = logger;
        }

        public async Task DownloadReport(RemoteBook remoteBook, int? downloadClientId)
        {
            var filterBlockedClients = remoteBook.Release.PendingReleaseReason == PendingReleaseReason.DownloadClientUnavailable;

            var tags = remoteBook.Author?.Tags;

            var downloadClient = downloadClientId.HasValue
                ? _downloadClientProvider.Get(downloadClientId.Value)
                : _downloadClientProvider.GetDownloadClient(remoteBook.Release.DownloadProtocol, remoteBook.Release.IndexerId, filterBlockedClients, tags);

            await DownloadReport(remoteBook, downloadClient, false);
        }

        public async Task AdoptExistingTorrent(RemoteBook remoteBook, int? downloadClientId)
        {
            var filterBlockedClients = remoteBook.Release.PendingReleaseReason == PendingReleaseReason.DownloadClientUnavailable;

            var tags = remoteBook.Author?.Tags;

            var downloadClient = downloadClientId.HasValue
                ? _downloadClientProvider.Get(downloadClientId.Value)
                : _downloadClientProvider.GetDownloadClient(remoteBook.Release.DownloadProtocol, remoteBook.Release.IndexerId, filterBlockedClients, tags);

            await DownloadReport(remoteBook, downloadClient, true);
        }

        private async Task DownloadReport(RemoteBook remoteBook, IDownloadClient downloadClient, bool adoptExistingTorrent)
        {
            Ensure.That(remoteBook.Author, () => remoteBook.Author).IsNotNull();
            Ensure.That(remoteBook.Books, () => remoteBook.Books).HasItems();

            var downloadTitle = remoteBook.Release.Title;

            if (downloadClient == null)
            {
                throw new DownloadClientUnavailableException($"{remoteBook.Release.DownloadProtocol} Download client isn't configured yet");
            }

            var existingTorrent = FindExistingTorrent(remoteBook, downloadClient, adoptExistingTorrent);

            if (existingTorrent != null && !adoptExistingTorrent)
            {
                throw new ExistingTorrentFoundException(downloadClient.Definition.Name, existingTorrent);
            }

            if (existingTorrent == null && adoptExistingTorrent)
            {
                throw new ExistingTorrentNotFoundException("The matching torrent is no longer available in the selected download client. Search again before grabbing it.");
            }

            // Get the seed configuration for this release.
            remoteBook.SeedConfiguration = _seedConfigProvider.GetSeedConfiguration(remoteBook);

            // Limit grabs to 2 per second. Adopting a torrent already in the client does not request the indexer.
            if (existingTorrent == null && remoteBook.Release.DownloadUrl.IsNotNullOrWhiteSpace() && !remoteBook.Release.DownloadUrl.StartsWith("magnet:"))
            {
                var url = new HttpUri(remoteBook.Release.DownloadUrl);
                await _rateLimitService.WaitAndPulseAsync(url.Host, TimeSpan.FromSeconds(2));
            }

            IIndexer indexer = null;

            if (!adoptExistingTorrent && remoteBook.Release.IndexerId > 0)
            {
                indexer = _indexerFactory.GetInstance(_indexerFactory.Get(remoteBook.Release.IndexerId));
            }

            string downloadClientId;
            try
            {
                if (existingTorrent != null)
                {
                    downloadClientId = existingTorrent.DownloadId;
                    _logger.Info("Adopting existing torrent {0} from {1} for {2}", existingTorrent.DownloadId, downloadClient.Definition.Name, remoteBook);
                }
                else
                {
                    downloadClientId = await downloadClient.Download(remoteBook, indexer);
                }
            }
            catch (ReleaseUnavailableException)
            {
                _logger.Trace("Release {0} no longer available on indexer.", remoteBook);
                throw;
            }
            catch (ReleaseBlockedException)
            {
                _logger.Trace("Release {0} previously added to blocklist, not sending to download client again.", remoteBook);
                throw;
            }
            catch (DownloadClientRejectedReleaseException)
            {
                _logger.Trace("Release {0} rejected by download client, possible duplicate.", remoteBook);
                throw;
            }
            catch (ReleaseDownloadException ex)
            {
                if (ex.InnerException is TooManyRequestsException http429)
                {
                    _indexerStatusService.RecordFailure(remoteBook.Release.IndexerId, http429.RetryAfter);
                }
                else
                {
                    _indexerStatusService.RecordFailure(remoteBook.Release.IndexerId);
                }

                throw;
            }

            _downloadClientStatusService.RecordSuccess(downloadClient.Definition.Id);
            if (existingTorrent == null)
            {
                _indexerStatusService.RecordSuccess(remoteBook.Release.IndexerId);
            }

            var bookGrabbedEvent = new BookGrabbedEvent(remoteBook);
            bookGrabbedEvent.DownloadClient = downloadClient.Name;
            bookGrabbedEvent.DownloadClientId = downloadClient.Definition.Id;
            bookGrabbedEvent.DownloadClientName = downloadClient.Definition.Name;

            if (downloadClientId.IsNotNullOrWhiteSpace())
            {
                bookGrabbedEvent.DownloadId = downloadClientId;
            }

            if (existingTorrent != null)
            {
                _logger.ProgressInfo("Adopted existing torrent from {0}. {1}", downloadClient.Definition.Name, downloadTitle);
            }
            else
            {
                _logger.ProgressInfo("Report sent to {0} from indexer {1}. {2}", downloadClient.Definition.Name, remoteBook.Release.Indexer, downloadTitle);
            }

            _eventAggregator.PublishEvent(bookGrabbedEvent);
        }

        private DownloadClientItem FindExistingTorrent(RemoteBook remoteBook, IDownloadClient downloadClient, bool required)
        {
            if (downloadClient.Protocol != DownloadProtocol.Torrent || remoteBook.Release is not TorrentInfo torrentInfo || torrentInfo.InfoHash.IsNullOrWhiteSpace())
            {
                return null;
            }

            if (required)
            {
                return FindTorrent(downloadClient, torrentInfo.InfoHash);
            }

            try
            {
                return FindTorrent(downloadClient, torrentInfo.InfoHash);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to check {0} for an existing torrent before grabbing {1}", downloadClient.Definition.Name, remoteBook);
                return null;
            }
        }

        private DownloadClientItem FindTorrent(IDownloadClient downloadClient, string infoHash)
        {
            return downloadClient.GetItems()?.FirstOrDefault(item =>
                item != null && string.Equals(item.DownloadId, infoHash, StringComparison.OrdinalIgnoreCase));
        }
    }
}
