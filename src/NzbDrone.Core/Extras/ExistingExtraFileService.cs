using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Extras
{
    public class ExistingExtraFileService : IHandle<AuthorScannedEvent>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskScanService _diskScanService;
        private readonly List<IImportExistingExtraFiles> _existingExtraFileImporters;
        private readonly Logger _logger;

        public ExistingExtraFileService(IDiskProvider diskProvider,
                                        IDiskScanService diskScanService,
                                        IEnumerable<IImportExistingExtraFiles> existingExtraFileImporters,
                                        Logger logger)
        {
            _diskProvider = diskProvider;
            _diskScanService = diskScanService;
            _existingExtraFileImporters = existingExtraFileImporters.OrderBy(e => e.Order).ToList();
            _logger = logger;
        }

        public void Handle(AuthorScannedEvent message)
        {
            var author = message.Author;
            var extraFiles = new List<ExtraFile>();

            var authorPaths = AuthorLocationResolver.GetPaths(author)
                .Where(_diskProvider.FolderExists)
                .Distinct(PathEqualityComparer.Instance)
                .ToList();

            if (authorPaths.Count == 0)
            {
                return;
            }

            _logger.Debug("Looking for existing extra files in {0}", string.Join(", ", authorPaths));

            var possibleExtraFiles = authorPaths
                .SelectMany(path => _diskScanService.FilterPaths(path, _diskScanService.GetNonBookFiles(path)))
                .Distinct(PathEqualityComparer.Instance)
                .ToList();

            var filteredFiles = possibleExtraFiles;
            var importedFiles = new List<string>();

            foreach (var existingExtraFileImporter in _existingExtraFileImporters)
            {
                var imported = existingExtraFileImporter.ProcessFiles(author, filteredFiles, importedFiles);

                importedFiles.AddRange(imported.Select(f => Path.Combine(author.Path, f.RelativePath)));
            }

            _logger.Info("Found {0} extra files", extraFiles.Count);
        }
    }
}
