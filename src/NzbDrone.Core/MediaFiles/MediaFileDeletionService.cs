using System;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDeleteMediaFiles
    {
        void DeleteTrackFile(Author author, BookFile bookFile);
        void DeleteTrackFile(BookFile bookFile, string subfolder = "");
    }

    public class MediaFileDeletionService : IDeleteMediaFiles,
                                            IHandle<AuthorDeletedEvent>,
                                            IHandleAsync<AuthorDeletedEvent>,
                                            IHandleAsync<BookDeletedEvent>,
                                            IHandle<BookFileDeletedEvent>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IAuthorService _authorService;
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IRootFolderService _rootFolderService;
        private readonly ICalibreProxy _calibre;
        private readonly Logger _logger;

        public MediaFileDeletionService(IDiskProvider diskProvider,
                                        IRecycleBinProvider recycleBinProvider,
                                        IMediaFileService mediaFileService,
                                        IAuthorService authorService,
                                        IConfigService configService,
                                        IEventAggregator eventAggregator,
                                        IRootFolderService rootFolderService,
                                        ICalibreProxy calibre,
                                        Logger logger)
        {
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _authorService = authorService;
            _configService = configService;
            _eventAggregator = eventAggregator;
            _rootFolderService = rootFolderService;
            _calibre = calibre;
            _logger = logger;
        }

        public void DeleteTrackFile(Author author, BookFile bookFile)
        {
            var fullPath = bookFile.Path;
            if (_diskProvider.FileExists(fullPath))
            {
                var rootFolder = _rootFolderService.GetBestRootFolder(fullPath);

                if (rootFolder == null || !_diskProvider.FolderExists(rootFolder.Path))
                {
                    var missingRoot = rootFolder?.Path ?? fullPath.GetParentPath();
                    _logger.Warn("Author's root folder ({0}) doesn't exist or isn't configured.", missingRoot);
                    throw new NzbDroneClientException(HttpStatusCode.Conflict, "Author's root folder ({0}) doesn't exist or isn't configured.", missingRoot);
                }

                if (_diskProvider.GetDirectories(rootFolder.Path).Empty())
                {
                    _logger.Warn("Author's root folder ({0}) is empty.", rootFolder.Path);
                    throw new NzbDroneClientException(HttpStatusCode.Conflict, "Author's root folder ({0}) is empty.", rootFolder.Path);
                }

                var subfolder = rootFolder.Path.GetRelativePath(_diskProvider.GetParentFolder(fullPath));
                DeleteTrackFile(bookFile, subfolder);
            }
            else
            {
                // delete from db even if the author folder is missing
                _mediaFileService.Delete(bookFile, DeleteMediaFileReason.Manual);
            }
        }

        public void DeleteTrackFile(BookFile bookFile, string subfolder = "")
        {
            var fullPath = bookFile.Path;

            if (_diskProvider.FileExists(fullPath))
            {
                _logger.Info("Deleting book file: {0}", fullPath);
                DeleteFile(bookFile, subfolder);
            }

            // Delete the track file from the database to clean it up even if the file was already deleted
            _mediaFileService.Delete(bookFile, DeleteMediaFileReason.Manual);

            _eventAggregator.PublishEvent(new DeleteCompletedEvent());
        }

        private void DeleteFile(BookFile bookFile, string subfolder = "")
        {
            var rootFolder = _rootFolderService.GetBestRootFolder(bookFile.Path);
            var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;

            try
            {
                if (!isCalibre)
                {
                    _recycleBinProvider.DeleteFile(bookFile.Path, subfolder);
                }
                else
                {
                    _calibre.DeleteBook(bookFile, rootFolder.CalibreSettings);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Unable to delete book file");
                throw new NzbDroneClientException(HttpStatusCode.InternalServerError, "Unable to delete book file");
            }
        }

        [EventHandleOrder(EventHandleOrder.First)]
        public void Handle(AuthorDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                var author = message.Author;

                var rootFolder = _rootFolderService.GetBestRootFolder(message.Author.Path);
                var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;

                if (isCalibre)
                {
                    // use metadataId instead of authorId so that query works even after author deleted
                    var books = _mediaFileService.GetFilesByAuthorMetadataId(author.AuthorMetadataId);
                    _calibre.DeleteBooks(books, rootFolder.CalibreSettings);
                }
            }
        }

        public void HandleAsync(AuthorDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                var author = message.Author;

                var rootFolder = _rootFolderService.GetBestRootFolder(message.Author.Path);
                var isCalibre = rootFolder.IsCalibreLibrary && rootFolder.CalibreSettings != null;

                if (!isCalibre)
                {
                    var authorPaths = AuthorLocationResolver.GetPaths(author);
                    var allAuthorPaths = _authorService.AllAuthorLocationPaths();

                    foreach (var authorPath in authorPaths)
                    {
                        foreach (var other in allAuthorPaths)
                        {
                            if (other.Key == author.Id)
                            {
                                continue;
                            }

                            if (authorPath.IsParentPath(other.Value) || other.Value.IsParentPath(authorPath))
                            {
                                _logger.Error("Author path: '{0}' overlaps another author's library path, not deleting files.", authorPath);
                                return;
                            }

                            if (authorPath.PathEquals(other.Value))
                            {
                                _logger.Error("Author path: '{0}' is the same as another author, not deleting files.", authorPath);
                                return;
                            }
                        }
                    }

                    var topLevelPaths = authorPaths
                        .Where(path => !authorPaths.Any(other => !other.PathEquals(path) && other.IsParentPath(path)))
                        .ToList();

                    foreach (var authorPath in topLevelPaths)
                    {
                        if (_diskProvider.FolderExists(authorPath))
                        {
                            _recycleBinProvider.DeleteFolder(authorPath);
                        }
                    }

                    _eventAggregator.PublishEvent(new DeleteCompletedEvent());
                }
            }
        }

        public void HandleAsync(BookDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                var files = _mediaFileService.GetFilesByBook(message.Book.Id);
                foreach (var file in files)
                {
                    DeleteFile(file);
                }
            }
        }

        [EventHandleOrder(EventHandleOrder.Last)]
        public void Handle(BookFileDeletedEvent message)
        {
            if (message.Reason == DeleteMediaFileReason.Upgrade)
            {
                return;
            }

            if (_configService.DeleteEmptyFolders)
            {
                var author = message.BookFile.Author.Value;
                var bookFolder = message.BookFile.Path.GetParentPath();
                var authorPath = AuthorLocationResolver.GetPathForFile(author, message.BookFile.Path);

                if (_diskProvider.GetFiles(authorPath, true).Empty())
                {
                    _diskProvider.DeleteFolder(authorPath, true);
                }
                else if (_diskProvider.GetFiles(bookFolder, true).Empty())
                {
                    _diskProvider.RemoveEmptySubfolders(bookFolder);
                }
            }
        }
    }
}
