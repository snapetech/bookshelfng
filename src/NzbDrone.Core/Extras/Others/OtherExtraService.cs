using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Extras.Others
{
    public class OtherExtraService : ExtraFileManager<OtherExtraFile>
    {
        private readonly IConfigService _configService;
        private readonly IDiskProvider _sidecarDiskProvider;
        private readonly IOtherExtraFileService _otherExtraFileService;
        private readonly IMediaFileAttributeService _mediaFileAttributeService;
        private readonly Logger _sidecarLogger;

        public OtherExtraService(IConfigService configService,
                                 IDiskProvider diskProvider,
                                 IDiskTransferService diskTransferService,
                                 IOtherExtraFileService otherExtraFileService,
                                 IMediaFileAttributeService mediaFileAttributeService,
                                 Logger logger)
            : base(configService, diskProvider, diskTransferService, logger)
        {
            _configService = configService;
            _sidecarDiskProvider = diskProvider;
            _otherExtraFileService = otherExtraFileService;
            _mediaFileAttributeService = mediaFileAttributeService;
            _sidecarLogger = logger;
        }

        public override int Order => 2;

        public override IEnumerable<ExtraFile> CreateAfterAuthorScan(Author author, List<BookFile> bookFiles)
        {
            return Enumerable.Empty<ExtraFile>();
        }

        public override IEnumerable<ExtraFile> CreateAfterBookImport(Author author, BookFile bookFile)
        {
            return Enumerable.Empty<ExtraFile>();
        }

        public override IEnumerable<ExtraFile> CreateAfterBookImport(Author author, Book book, string authorFolder, string bookFolder)
        {
            return Enumerable.Empty<ExtraFile>();
        }

        public override IEnumerable<ExtraFile> MoveFilesAfterRename(Author author, List<BookFile> bookFiles, List<RenamedBookFile> renamedFiles)
        {
            var extraFiles = _otherExtraFileService.GetFilesByAuthor(author.Id);
            var movedFiles = new List<OtherExtraFile>();

            foreach (var bookFile in bookFiles)
            {
                var extraFilesForTrackFile = extraFiles.Where(m => m.BookFileId == bookFile.Id).ToList();

                foreach (var extraFile in extraFilesForTrackFile)
                {
                    movedFiles.AddIfNotNull(MoveFile(author, bookFile, extraFile));
                }
            }

            _otherExtraFileService.Upsert(movedFiles);
            MoveUntrackedExtraFiles(author, bookFiles, renamedFiles);

            return movedFiles;
        }

        private void MoveUntrackedExtraFiles(Author author, List<BookFile> bookFiles, List<RenamedBookFile> renamedFiles)
        {
            if (!_configService.MoveExtraFilesOnRename || string.IsNullOrWhiteSpace(_configService.ExtraFileExtensions))
            {
                return;
            }

            var extensions = _configService.ExtraFileExtensions
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(extension => extension.Trim().TrimStart('.'))
                .Where(extension => extension.Length > 0)
                .Select(extension => $".{extension}")
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            var directoryMoves = renamedFiles
                .Where(file => !string.IsNullOrWhiteSpace(file.PreviousPath) && !string.IsNullOrWhiteSpace(file.BookFile?.Path))
                .Select(file => new
                {
                    Source = Path.GetDirectoryName(file.PreviousPath),
                    Destination = Path.GetDirectoryName(file.BookFile.Path)
                })
                .Where(move => !string.IsNullOrWhiteSpace(move.Source) &&
                               !string.IsNullOrWhiteSpace(move.Destination) &&
                               !move.Source.PathEquals(move.Destination))
                .GroupBy(move => move.Source, System.StringComparer.OrdinalIgnoreCase);

            foreach (var directoryGroup in directoryMoves)
            {
                var sourceDirectory = directoryGroup.Key;
                var destinations = directoryGroup.Select(move => move.Destination)
                    .Distinct(System.StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (destinations.Count != 1 || bookFiles.Any(file => Path.GetDirectoryName(file.Path).PathEquals(sourceDirectory)))
                {
                    continue;
                }

                var destinationDirectory = destinations[0];

                foreach (var path in _sidecarDiskProvider.GetFiles(sourceDirectory, false))
                {
                    if (!extensions.Contains(Path.GetExtension(path)))
                    {
                        continue;
                    }

                    var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(path));

                    if (_sidecarDiskProvider.FileExists(destinationPath))
                    {
                        _sidecarLogger.Warn("Unable to move extra file after rename because the destination already exists: {0}", destinationPath);
                        continue;
                    }

                    try
                    {
                        _sidecarLogger.Debug("Moving extra file after rename: {0} to {1}", path, destinationPath);
                        _sidecarDiskProvider.MoveFile(path, destinationPath);
                    }
                    catch (System.Exception ex)
                    {
                        _sidecarLogger.Warn(ex, "Unable to move extra file after rename: {0}", path);
                    }
                }
            }
        }

        public override ExtraFile Import(Author author, BookFile bookFile, string path, string extension, bool readOnly)
        {
            var extraFile = ImportFile(author, bookFile, path, readOnly, extension, null);

            _mediaFileAttributeService.SetFilePermissions(path);
            _otherExtraFileService.Upsert(extraFile);

            return extraFile;
        }
    }
}
