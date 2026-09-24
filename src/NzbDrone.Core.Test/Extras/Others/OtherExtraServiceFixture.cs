using System.Collections.Generic;
using System.IO;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Extras.Others
{
    [TestFixture]
    public class OtherExtraServiceFixture : CoreTest<OtherExtraService>
    {
        private readonly string _sourceDirectory = Path.Combine(Path.GetTempPath(), "Bookshelf", "Old");
        private readonly string _destinationDirectory = Path.Combine(Path.GetTempPath(), "Bookshelf", "New");

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(config => config.MoveExtraFilesOnRename)
                .Returns(true);

            Mocker.GetMock<IConfigService>()
                .SetupGet(config => config.ExtraFileExtensions)
                .Returns("nfo");

            Mocker.GetMock<IOtherExtraFileService>()
                .Setup(service => service.GetFilesByAuthor(It.IsAny<int>()))
                .Returns(new List<OtherExtraFile>());
        }

        [Test]
        public void should_move_configured_sidecars_after_audio_leaves_source_folder()
        {
            var sidecarPath = Path.Combine(_sourceDirectory, "notes.nfo");
            var destinationPath = Path.Combine(_destinationDirectory, "notes.nfo");
            var renamedBookFile = GivenRenamedBookFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.FolderExists(_sourceDirectory))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetFiles(_sourceDirectory, false))
                .Returns(new List<string> { sidecarPath });

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.FileExists(destinationPath))
                .Returns(false);

            Subject.MoveFilesAfterRename(
                new Author { Id = 1 },
                new List<BookFile> { renamedBookFile.BookFile },
                new List<RenamedBookFile> { renamedBookFile });

            Mocker.GetMock<IDiskProvider>()
                .Verify(disk => disk.MoveFile(sidecarPath, destinationPath), Times.Once());
        }

        [Test]
        public void should_leave_sidecars_when_audio_remains_in_source_folder()
        {
            var remainingAudioPath = Path.Combine(_sourceDirectory, "another-book.m4b");
            var sidecarPath = Path.Combine(_sourceDirectory, "notes.nfo");
            var renamedBookFile = GivenRenamedBookFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.FolderExists(_sourceDirectory))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.GetFiles(_sourceDirectory, false))
                .Returns(new List<string> { remainingAudioPath, sidecarPath });

            Subject.MoveFilesAfterRename(
                new Author { Id = 1 },
                new List<BookFile> { renamedBookFile.BookFile },
                new List<RenamedBookFile> { renamedBookFile });

            Mocker.GetMock<IDiskProvider>()
                .Verify(disk => disk.MoveFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_skip_sidecar_lookup_when_source_folder_no_longer_exists()
        {
            var renamedBookFile = GivenRenamedBookFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(disk => disk.FolderExists(_sourceDirectory))
                .Returns(false);

            Subject.MoveFilesAfterRename(
                new Author { Id = 1 },
                new List<BookFile> { renamedBookFile.BookFile },
                new List<RenamedBookFile> { renamedBookFile });

            Mocker.GetMock<IDiskProvider>()
                .Verify(disk => disk.GetFiles(_sourceDirectory, false), Times.Never());
        }

        private RenamedBookFile GivenRenamedBookFile()
        {
            var previousPath = Path.Combine(_sourceDirectory, "book.m4b");
            var bookFile = new BookFile { Id = 1, Path = Path.Combine(_destinationDirectory, "book.m4b") };

            return new RenamedBookFile { BookFile = bookFile, PreviousPath = previousPath };
        }
    }
}
