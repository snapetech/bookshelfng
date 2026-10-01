using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles.Commands
{
    public class RetryUnmappedFilesCommand : Command
    {
        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
        public override bool IsTypeExclusive => true;
    }

    public class RetryUnmappedFilesCommandService : IExecute<RetryUnmappedFilesCommand>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskScanService _diskScanService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IRootFolderService _rootFolderService;
        private readonly Logger _logger;

        public RetryUnmappedFilesCommandService(IDiskProvider diskProvider,
                                                IDiskScanService diskScanService,
                                                IMediaFileService mediaFileService,
                                                IRootFolderService rootFolderService,
                                                Logger logger)
        {
            _diskProvider = diskProvider;
            _diskScanService = diskScanService;
            _mediaFileService = mediaFileService;
            _rootFolderService = rootFolderService;
            _logger = logger;
        }

        public void Execute(RetryUnmappedFilesCommand message)
        {
            var filesToRetry = _mediaFileService.GetUnmappedFiles()
                .Where(x => _diskProvider.FileExists(x.Path) && _rootFolderService.GetBestRootFolder(x.Path) != null)
                .ToList();

            if (!filesToRetry.Any())
            {
                _logger.ProgressInfo("No existing unmapped files need a retry");
                return;
            }

            var initialPaths = filesToRetry.Select(x => x.Path)
                .ToHashSet(PathEqualityComparer.Instance);
            var folders = GetScanFolders(filesToRetry.Select(x => x.Path));

            _logger.ProgressInfo("Retrying catalog matches for {0} unmapped files", initialPaths.Count);
            _diskScanService.Scan(folders, FilterFilesType.Unmapped, true);

            var remainingPaths = _mediaFileService.GetUnmappedFiles()
                .Select(x => x.Path)
                .ToHashSet(PathEqualityComparer.Instance);
            var stillUnmapped = initialPaths.Count(path => remainingPaths.Contains(path));
            var mapped = initialPaths.Count - stillUnmapped;

            _logger.ProgressInfo("Mapped {0} of {1} existing unmapped files; {2} still need a match", mapped, initialPaths.Count, stillUnmapped);
        }

        private List<string> GetScanFolders(IEnumerable<string> paths)
        {
            var folders = paths.Select(Path.GetDirectoryName)
                .Where(x => x.IsNotNullOrWhiteSpace() && _diskProvider.FolderExists(x))
                .Distinct(PathEqualityComparer.Instance)
                .OrderBy(x => x.Length)
                .ToList();
            var scanFolders = new List<string>();

            foreach (var folder in folders)
            {
                if (!scanFolders.Any(parent => PathEqualityComparer.Instance.Equals(parent, folder) || parent.IsParentPath(folder)))
                {
                    scanFolders.Add(folder);
                }
            }

            return scanFolders;
        }
    }
}
