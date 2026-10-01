using System;
using System.IO;
using System.IO.Abstractions;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.DiskTests
{
    public abstract class DiskProviderFixtureBase<TSubject> : TestBase<TSubject>
        where TSubject : class, IDiskProvider
    {
        [SetUp]
        public void BaseSetup()
        {
            Mocker.SetConstant<IFileSystem>(new FileSystem());
        }

        [Test]
        public void writealltext_should_truncate_existing()
        {
            var file = GetTempFilePath();

            Subject.WriteAllText(file, "A pretty long string");
            Subject.WriteAllText(file, "A short string");
            Subject.ReadAllText(file).Should().Be("A short string");
        }

        [Test]
        public void directory_exist_should_be_able_to_find_existing_folder()
        {
            Subject.FolderExists(TempFolder).Should().BeTrue();
        }

        [Test]
        public void directory_exist_should_be_able_to_find_existing_unc_share()
        {
            WindowsOnly();

            Subject.FolderExists(@"\\localhost\c$").Should().BeTrue();
        }

        [Test]
        public void directory_exist_should_not_be_able_to_find_none_existing_folder()
        {
            Subject.FolderExists(@"C:\ThisBetterNotExist\".AsOsAgnostic()).Should().BeFalse();
        }

        protected abstract void SetWritePermissions(string path, bool writable);

        [Test]
        public void FolderWritable_should_return_true_for_writable_directory()
        {
            var tempFolder = GetTempFilePath();
            Directory.CreateDirectory(tempFolder);

            var result = Subject.FolderWritable(tempFolder);

            result.Should().BeTrue();
        }

        [Test]
        public void FolderWritable_should_work_near_legacy_windows_path_limit()
        {
            var tempFolder = GetTempFilePath();
            Directory.CreateDirectory(tempFolder);

            var longFolder = tempFolder;
            while (longFolder.Length + 11 < 235)
            {
                var childFolder = Path.Combine(longFolder, "abcdefghij");
                Directory.CreateDirectory(childFolder);
                longFolder = childFolder;
            }

            const int LegacyProbeNameLength = 55;
            const int ShortProbeNameLength = 12;
            Path.Combine(longFolder, "readarr_write_test.txt").Length.Should().BeLessThan(260);
            (longFolder.Length + 1 + LegacyProbeNameLength).Should().BeGreaterOrEqualTo(260);
            (longFolder.Length + 1 + ShortProbeNameLength).Should().BeLessThan(260);

            var fileSystem = new FileSystem();
            var fileStreamFactory = new Mock<IFileStreamFactory>();
            fileStreamFactory.Setup(f => f.New(It.IsAny<string>(), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>()))
                .Returns((string path, FileMode mode, FileAccess access, FileShare share) =>
                {
                    if (path.Length >= 260)
                    {
                        throw new PathTooLongException("Simulated legacy Windows MAX_PATH limit");
                    }

                    return fileSystem.FileStream.New(path, mode, access, share);
                });

            var wrappedFileSystem = new Mock<IFileSystem>();
            wrappedFileSystem.SetupGet(fs => fs.File).Returns(fileSystem.File);
            wrappedFileSystem.SetupGet(fs => fs.FileStream).Returns(fileStreamFactory.Object);
            Mocker.SetConstant(wrappedFileSystem.Object);

            var legacyProbePath = Path.Combine(longFolder, $"readarr_write_test_{Guid.NewGuid():N}.txt");
            legacyProbePath.Length.Should().BeGreaterOrEqualTo(260);
            Action openLegacyProbe = () => fileStreamFactory.Object.New(legacyProbePath, FileMode.Create, FileAccess.Write, FileShare.None);
            openLegacyProbe.Should().Throw<PathTooLongException>();

            Subject.FolderWritable(longFolder).Should().BeTrue();
        }

        [Test]
        public void FolderWritable_should_retry_probe_name_collision_without_modifying_existing_file()
        {
            var tempFolder = GetTempFilePath();
            Directory.CreateDirectory(tempFolder);

            var fileSystem = new FileSystem();
            var collidedPath = string.Empty;
            var firstOpen = true;
            var fileStreamFactory = new Mock<IFileStreamFactory>();
            fileStreamFactory.Setup(f => f.New(It.IsAny<string>(), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                .Returns((string path, FileMode mode, FileAccess access, FileShare share) =>
                {
                    if (firstOpen)
                    {
                        firstOpen = false;
                        collidedPath = path;
                        File.WriteAllText(path, "keep this existing file");
                        throw new IOException("Probe name already exists");
                    }

                    return fileSystem.FileStream.New(path, mode, access, share);
                });

            var wrappedFileSystem = new Mock<IFileSystem>();
            wrappedFileSystem.SetupGet(fs => fs.File).Returns(fileSystem.File);
            wrappedFileSystem.SetupGet(fs => fs.FileStream).Returns(fileStreamFactory.Object);
            Mocker.SetConstant(wrappedFileSystem.Object);

            try
            {
                Subject.FolderWritable(tempFolder).Should().BeTrue();
                File.ReadAllText(collidedPath).Should().Be("keep this existing file");
            }
            finally
            {
                if (File.Exists(collidedPath))
                {
                    File.Delete(collidedPath);
                }
            }
        }

        [Test]
        public void FolderWritable_should_return_true_when_test_file_cannot_be_deleted()
        {
            var tempFolder = GetTempFilePath();
            Directory.CreateDirectory(tempFolder);
            string testFilePath = null;
            var file = new Mock<IFile>();
            file.Setup(f => f.Delete(It.IsAny<string>()))
                .Callback<string>(path => testFilePath = path)
                .Throws(new UnauthorizedAccessException("Delete access denied"));
            var fileSystem = new Mock<IFileSystem>();
            fileSystem.SetupGet(f => f.File).Returns(file.Object);
            fileSystem.SetupGet(f => f.FileStream).Returns(new FileSystem().FileStream);
            Mocker.SetConstant(fileSystem.Object);

            try
            {
                var result = Subject.FolderWritable(tempFolder);

                ExceptionVerification.ExpectedWarns(1, "Unable to remove folder probe", "Delete access denied");
                testFilePath.Should().NotBeNull();
                File.Exists(testFilePath).Should().BeTrue();
                result.Should().BeTrue();
            }
            finally
            {
                if (testFilePath != null && File.Exists(testFilePath))
                {
                    File.Delete(testFilePath);
                }
            }
        }

        [Test]
        public void FolderWritable_should_log_filesystem_error_when_write_probe_fails()
        {
            var tempFolder = GetTempFilePath();
            Directory.CreateDirectory(tempFolder);

            var fileStream = new Mock<IFileStreamFactory>();
            fileStream.Setup(f => f.New(It.IsAny<string>(), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                .Throws(new UnauthorizedAccessException("Create access denied"));
            var fileSystem = new Mock<IFileSystem>();
            fileSystem.SetupGet(f => f.File).Returns(new FileSystem().File);
            fileSystem.SetupGet(f => f.FileStream).Returns(fileStream.Object);
            Mocker.SetConstant(fileSystem.Object);

            var result = Subject.FolderWritable(tempFolder);

            result.Should().BeFalse();
            ExceptionVerification.ExpectedWarns(1, "Failed to write folder probe", "Create access denied", $"process account '{Environment.UserName}'", "HResult 0x");
        }

        [Test]
        public void FolderWritable_should_return_false_for_unwritable_directory()
        {
            var tempFolder = GetTempFilePath();
            Directory.CreateDirectory(tempFolder);

            SetWritePermissions(tempFolder, false);
            try
            {
                var result = Subject.FolderWritable(tempFolder);

                result.Should().BeFalse();
            }
            finally
            {
                SetWritePermissions(tempFolder, true);
            }
        }

        [Test]
        public void MoveFile_should_overwrite_existing_file()
        {
            var source1 = GetTempFilePath();
            var source2 = GetTempFilePath();
            var destination = GetTempFilePath();

            File.WriteAllText(source1, "SourceFile1");
            File.WriteAllText(source2, "SourceFile2");

            Subject.MoveFile(source1, destination);
            Subject.MoveFile(source2, destination, true);

            File.Exists(destination).Should().BeTrue();
        }

        [Test]
        public void MoveFile_should_not_move_overwrite_itself()
        {
            var source = GetTempFilePath();

            File.WriteAllText(source, "SourceFile1");

            Assert.Throws<IOException>(() => Subject.MoveFile(source, source, true));

            File.Exists(source).Should().BeTrue();
        }

        [Test]
        public void should_be_able_to_move_read_only_file()
        {
            var source = GetTempFilePath();
            var destination = GetTempFilePath();

            Subject.WriteAllText(source, "SourceFile");
            Subject.WriteAllText(destination, "DestinationFile");

            File.SetAttributes(source, FileAttributes.ReadOnly);
            File.SetAttributes(destination, FileAttributes.ReadOnly);

            Subject.MoveFile(source, destination, true);
        }

        [Test]
        public void should_be_able_to_delete_directory_with_read_only_file()
        {
            var sourceDir = GetTempFilePath();
            var source = Path.Combine(sourceDir, "test.txt");

            Directory.CreateDirectory(sourceDir);

            Subject.WriteAllText(source, "SourceFile");

            File.SetAttributes(source, FileAttributes.ReadOnly);

            Subject.DeleteFolder(sourceDir, true);

            Directory.Exists(sourceDir).Should().BeFalse();
        }

        [Test]
        public void should_be_able_to_delete_nested_empty_subdirs()
        {
            var authorDir = Path.Combine(GetTempFilePath(), "Author");
            var bookDir = Path.Combine(authorDir, "Book");

            Directory.CreateDirectory(Path.Combine(bookDir));
            Directory.CreateDirectory(Path.Combine(bookDir, "Book"));
            Directory.CreateDirectory(Path.Combine(bookDir, "Book", "CD1"));
            Directory.CreateDirectory(Path.Combine(bookDir, "Book", "CD2"));

            Subject.RemoveEmptySubfolders(authorDir);
            Directory.Exists(bookDir).Should().BeFalse();
        }

        [Test]
        public void empty_folder_should_return_folder_modified_date()
        {
            var tempfolder = new DirectoryInfo(TempFolder);
            Subject.FolderGetLastWrite(TempFolder).Should().Be(tempfolder.LastWriteTimeUtc);
        }

        [Test]
        public void folder_should_return_correct_value_for_last_write()
        {
            var testDir = GetTempFilePath();
            var testFile = Path.Combine(testDir, Path.GetRandomFileName());

            Directory.CreateDirectory(testDir);

            Subject.FolderSetLastWriteTime(TempFolder, DateTime.UtcNow.AddMinutes(-5));

            TestLogger.Info("Path is: {0}", testFile);

            Subject.WriteAllText(testFile, "Test");

            Subject.FolderGetLastWrite(TempFolder).Should().BeOnOrAfter(DateTime.UtcNow.AddMinutes(-1));
            Subject.FolderGetLastWrite(TempFolder).Should().BeBefore(DateTime.UtcNow.AddMinutes(1));
        }

        [Test]
        public void should_return_false_for_unlocked_file()
        {
            var testFile = GetTempFilePath();
            Subject.WriteAllText(testFile, default(Guid).ToString());

            Subject.IsFileLocked(testFile).Should().BeFalse();
        }

        [Test]
        public void should_return_false_for_unlocked_and_readonly_file()
        {
            var testFile = GetTempFilePath();
            Subject.WriteAllText(testFile, default(Guid).ToString());

            File.SetAttributes(testFile, FileAttributes.ReadOnly);

            Subject.IsFileLocked(testFile).Should().BeFalse();
        }

        [Test]
        public void should_return_true_for_unlocked_file()
        {
            var testFile = GetTempFilePath();
            Subject.WriteAllText(testFile, default(Guid).ToString());

            using (var file = File.OpenWrite(testFile))
            {
                Subject.IsFileLocked(testFile).Should().BeTrue();
            }
        }

        [Test]
        public void should_be_able_to_set_permission_from_parrent()
        {
            var testFile = GetTempFilePath();
            Subject.WriteAllText(testFile, default(Guid).ToString());

            Subject.InheritFolderPermissions(testFile);
        }

        [Test]
        public void should_be_set_last_file_write()
        {
            var testFile = GetTempFilePath();
            Subject.WriteAllText(testFile, default(Guid).ToString());

            var lastWriteTime = DateTime.SpecifyKind(new DateTime(2012, 1, 2), DateTimeKind.Utc);

            Subject.FileSetLastWriteTime(testFile, lastWriteTime);
            Subject.FileGetLastWrite(testFile).Should().Be(lastWriteTime);
        }

        [Test]
        public void GetParentFolder_should_remove_trailing_slash_before_getting_parent_folder()
        {
            var path = @"C:\Test\Music\".AsOsAgnostic();
            var parent = @"C:\Test".AsOsAgnostic();

            Subject.GetParentFolder(path).Should().Be(parent);
        }

        [Test]
        public void RemoveEmptySubfolders_should_remove_nested_empty_folder()
        {
            var mainDir = GetTempFilePath();
            var subDir1 = Path.Combine(mainDir, "depth1");
            var subDir2 = Path.Combine(subDir1, "depth2");
            Directory.CreateDirectory(subDir2);

            Subject.RemoveEmptySubfolders(mainDir);

            Directory.Exists(mainDir).Should().Be(true);
            Directory.Exists(subDir1).Should().Be(false);
        }

        [Test]
        public void RemoveEmptySubfolders_should_not_remove_nested_nonempty_folder()
        {
            var mainDir = GetTempFilePath();
            var subDir1 = Path.Combine(mainDir, "depth1");
            var subDir2 = Path.Combine(subDir1, "depth2");
            var file = Path.Combine(subDir1, "file1.txt");
            Directory.CreateDirectory(subDir2);
            File.WriteAllText(file, "I should not be deleted");

            Subject.RemoveEmptySubfolders(mainDir);

            Directory.Exists(mainDir).Should().Be(true);
            Directory.Exists(subDir1).Should().Be(true);
            Directory.Exists(subDir2).Should().Be(false);
            File.Exists(file).Should().Be(true);
        }

        private void DoHardLinkRename(FileShare fileShare)
        {
            var sourceDir = GetTempFilePath();
            var source = Path.Combine(sourceDir, "test.txt");
            var destination = Path.Combine(sourceDir, "destination.txt");
            var rename = Path.Combine(sourceDir, "rename.txt");

            Directory.CreateDirectory(sourceDir);

            File.WriteAllText(source, "SourceFile");

            Subject.TryCreateHardLink(source, destination).Should().BeTrue();

            using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read, fileShare))
            {
                stream.ReadByte();

                Subject.MoveFile(destination, rename);

                stream.ReadByte();
            }

            File.Exists(rename).Should().BeTrue();
            File.Exists(destination).Should().BeFalse();

            File.AppendAllText(source, "Test");
            File.ReadAllText(rename).Should().Be("SourceFileTest");
        }

        [Test]
        public void should_be_able_to_rename_open_hardlinks_with_fileshare_delete()
        {
            DoHardLinkRename(FileShare.Delete);
        }

        [Test]
        [Ignore("No longer behaving this way in a Windows 10 Feature Update")]
        public void should_not_be_able_to_rename_open_hardlinks_with_fileshare_none()
        {
            WindowsOnly();

            Assert.Throws<IOException>(() => DoHardLinkRename(FileShare.None));
        }

        [Test]
        [Ignore("No longer behaving this way in a Windows 10 Feature Update")]
        public void should_not_be_able_to_rename_open_hardlinks_with_fileshare_write()
        {
            WindowsOnly();

            Assert.Throws<IOException>(() => DoHardLinkRename(FileShare.Read));
        }
    }
}
