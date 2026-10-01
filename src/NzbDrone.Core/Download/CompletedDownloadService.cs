using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    public interface ICompletedDownloadService
    {
        void Check(TrackedDownload trackedDownload);
        void Import(TrackedDownload trackedDownload);
        bool VerifyImport(TrackedDownload trackedDownload, List<ImportResult> importResults);
    }

    public class CompletedDownloadService : ICompletedDownloadService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IHistoryService _historyService;
        private readonly IProvideImportItemService _provideImportItemService;
        private readonly IDownloadedBooksImportService _downloadedTracksImportService;
        private readonly ITrackedDownloadAlreadyImported _trackedDownloadAlreadyImported;
        private readonly IBookService _bookService;
        private readonly Logger _logger;

        public CompletedDownloadService(IEventAggregator eventAggregator,
                                        IHistoryService historyService,
                                        IProvideImportItemService provideImportItemService,
                                        IDownloadedBooksImportService downloadedTracksImportService,
                                        ITrackedDownloadAlreadyImported trackedDownloadAlreadyImported,
                                        IBookService bookService,
                                        Logger logger)
        {
            _eventAggregator = eventAggregator;
            _historyService = historyService;
            _provideImportItemService = provideImportItemService;
            _downloadedTracksImportService = downloadedTracksImportService;
            _trackedDownloadAlreadyImported = trackedDownloadAlreadyImported;
            _bookService = bookService;
            _logger = logger;
        }

        public void Check(TrackedDownload trackedDownload)
        {
            if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
            {
                return;
            }

            SetImportItem(trackedDownload);

            // Only process tracked downloads that are still downloading
            if (trackedDownload.State != TrackedDownloadState.Downloading)
            {
                return;
            }

            var historyItem = _historyService.MostRecentForDownloadId(trackedDownload.DownloadItem.DownloadId);

            if (historyItem == null && trackedDownload.DownloadItem.Category.IsNullOrWhiteSpace())
            {
                trackedDownload.Warn("Download wasn't grabbed by Readarr and not in a category, Skipping.");
                return;
            }

            if (!ValidatePath(trackedDownload))
            {
                return;
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;
        }

        public void Import(TrackedDownload trackedDownload)
        {
            SetImportItem(trackedDownload);

            if (!ValidatePath(trackedDownload))
            {
                return;
            }

            trackedDownload.State = TrackedDownloadState.Importing;

            var outputPath = trackedDownload.ImportItem.OutputPath.FullPath;
            var idOverrides = BuildIdentificationOverrides(trackedDownload.RemoteBook);
            var importResults = _downloadedTracksImportService.ProcessPath(outputPath, ImportMode.Auto, idOverrides, trackedDownload.DownloadItem);

            if (importResults.Empty())
            {
                trackedDownload.Warn("No files found are eligible for import in {0}", outputPath);
                trackedDownload.State = TrackedDownloadState.ImportPending;
                return;
            }

            if (VerifyImport(trackedDownload, importResults))
            {
                return;
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;

            if (importResults.Any(c => c.Result != ImportResultType.Imported))
            {
                trackedDownload.State = TrackedDownloadState.ImportFailed;
                var statusMessages = importResults
                    .Where(v => v.Result != ImportResultType.Imported && v.ImportDecision.Item != null)
                    .Select(v => new TrackedDownloadStatusMessage(Path.GetFileName(v.ImportDecision.Item.Path), v.Errors))
                    .ToArray();

                trackedDownload.Warn(statusMessages);
                _eventAggregator.PublishEvent(new BookImportIncompleteEvent(trackedDownload));
                return;
            }
        }

        public bool VerifyImport(TrackedDownload trackedDownload, List<ImportResult> importResults)
        {
            var allItemsImported = importResults.All(c => c.Result == ImportResultType.Imported) &&
                                   importResults.Count(c => c.Result == ImportResultType.Imported) >= Math.Max(1, trackedDownload.RemoteBook?.Books.Count ?? 1);

            if (allItemsImported)
            {
                _logger.Debug("All books were imported for {0}", trackedDownload.DownloadItem.Title);
                trackedDownload.State = TrackedDownloadState.Imported;

                var importedAuthorId = importResults.Where(x => x.Result == ImportResultType.Imported)
                    .Select(c => c.ImportDecision.Item.Author.Id)
                    .MostCommon();
                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, trackedDownload.RemoteBook?.Author.Id ?? importedAuthorId));
                return true;
            }

            // A book can contain several files. History may confirm a rejected result only when
            // that exact source file was imported in an earlier pass for this download.
            var atLeastOneFileImported = importResults.Any(c => c.Result == ImportResultType.Imported);

            var historyItems = _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId)
                                              .OrderByDescending(h => h.Date)
                                              .ToList();

            var allBooksImportedInHistory = _trackedDownloadAlreadyImported.IsImported(trackedDownload, historyItems);
            var allFilesImportedInHistory = importResults.All(result =>
            {
                if (result.Result == ImportResultType.Imported)
                {
                    return true;
                }

                var sourcePath = result.ImportDecision.Item?.Path;

                return sourcePath.IsNotNullOrWhiteSpace() && historyItems.Any(history =>
                    history.EventType == EntityHistoryEventType.BookFileImported &&
                    history.Data.TryGetValue("DroppedPath", out var importedPath) &&
                    importedPath.IsNotNullOrWhiteSpace() &&
                    importedPath.PathEquals(sourcePath));
            });

            if (allBooksImportedInHistory && allFilesImportedInHistory)
            {
                // Log different error messages depending on the circumstances, but treat both as fully imported, because that's the reality.
                // The second message shouldn't be logged in most cases, but continued reporting would indicate an ongoing issue.
                if (atLeastOneFileImported)
                {
                    _logger.Debug("All files were imported in history for {0}", trackedDownload.DownloadItem.Title);
                }
                else
                {
                    _logger.ForDebugEvent()
                           .Message("No files were just imported, but all source files were previously imported, possible issue with download history.")
                           .Property("AuthorId", trackedDownload.RemoteBook.Author.Id)
                           .Property("DownloadId", trackedDownload.DownloadItem.DownloadId)
                           .Property("Title", trackedDownload.DownloadItem.Title)
                           .Property("Path", trackedDownload.DownloadItem.OutputPath.ToString())
                           .Log();
                }

                trackedDownload.State = TrackedDownloadState.Imported;

                var importedAuthorId = historyItems.Where(x => x.EventType == EntityHistoryEventType.BookFileImported)
                    .Select(x => x.AuthorId)
                    .MostCommon();
                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, trackedDownload.RemoteBook?.Author.Id ?? importedAuthorId));

                return true;
            }

            _logger.Debug("Not all book files have been imported for {0}", trackedDownload.DownloadItem.Title);
            return false;
        }

        private void SetImportItem(TrackedDownload trackedDownload)
        {
            trackedDownload.ImportItem = _provideImportItemService.ProvideImportItem(trackedDownload.DownloadItem, trackedDownload.ImportItem);
        }

        private IdentificationOverrides BuildIdentificationOverrides(Parser.Model.RemoteBook remoteBook)
        {
            if (remoteBook == null)
            {
                return null;
            }

            var overrides = new IdentificationOverrides
            {
                Author = remoteBook.Author
            };

            // Only set Book override when the grab targeted exactly one book
            // AND we can confirm the book exists in the local database.
            // RemoteBook.Books may contain stale or unmapped objects that would
            // cause CandidateService.GetDbCandidatesByBook to return zero candidates.
            if (remoteBook.Books != null && remoteBook.Books.Count == 1)
            {
                var candidate = remoteBook.Books.First();
                if (candidate.Id > 0 && _bookService.GetBook(candidate.Id) != null)
                {
                    overrides.Book = candidate;
                }
            }

            return overrides;
        }

        private bool ValidatePath(TrackedDownload trackedDownload)
        {
            var downloadItemOutputPath = trackedDownload.ImportItem.OutputPath;

            if (downloadItemOutputPath.IsEmpty)
            {
                trackedDownload.Warn("Download doesn't contain intermediate path, Skipping.");
                return false;
            }

            if ((OsInfo.IsWindows && !downloadItemOutputPath.IsWindowsPath) ||
                (OsInfo.IsNotWindows && !downloadItemOutputPath.IsUnixPath))
            {
                trackedDownload.Warn("[{0}] is not a valid local path. You may need a Remote Path Mapping.", downloadItemOutputPath);
                return false;
            }

            return true;
        }
    }
}
