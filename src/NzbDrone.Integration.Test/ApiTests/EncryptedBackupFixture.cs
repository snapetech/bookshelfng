using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Backup;
using RestSharp;

namespace NzbDrone.Integration.Test.ApiTests
{
    [TestFixture]
    public class EncryptedBackupFixture : IntegrationTest
    {
        private const string Passphrase = "integration-backup-passphrase";

        [Test]
        public void should_export_and_restore_an_encrypted_backup()
        {
            var exportRequest = new RestRequest("system/backup/encrypted", Method.POST);
            exportRequest.AddHeader(EncryptedBackupService.PassphraseHeader, Passphrase);

            var exportResponse = RestClient.Execute(exportRequest);
            exportResponse.StatusCode.Should().Be(HttpStatusCode.OK, exportResponse.Content);
            exportResponse.ContentType.Should().Be("application/octet-stream");
            exportResponse.RawBytes.Should().NotBeNullOrEmpty();

            var backupDirectory = GetTempDirectory("EncryptedBackup");
            var encryptedPath = Path.Combine(backupDirectory, "portable-backup.enc");
            var archivePath = Path.Combine(backupDirectory, "portable-backup.zip");
            File.WriteAllBytes(encryptedPath, exportResponse.RawBytes);
            new EncryptedBackupService().Decrypt(encryptedPath, archivePath, Passphrase);

            using (var archive = ZipFile.OpenRead(archivePath))
            {
                archive.Entries.SingleOrDefault(entry => string.Equals(entry.Name, "Config.xml", StringComparison.OrdinalIgnoreCase)).Should().NotBeNull();
                archive.GetEntry("INFO").Should().NotBeNull();
            }

            var invalidRestoreRequest = new RestRequest("system/backup/restore/upload", Method.POST);
            invalidRestoreRequest.AddHeader(EncryptedBackupService.PassphraseHeader, "incorrect-backup-passphrase");
            invalidRestoreRequest.AddFile("restore", encryptedPath, "application/octet-stream");

            var invalidRestoreResponse = RestClient.Execute(invalidRestoreRequest);
            invalidRestoreResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest, invalidRestoreResponse.Content);

            var restoreRequest = new RestRequest("system/backup/restore/upload", Method.POST);
            restoreRequest.AddHeader(EncryptedBackupService.PassphraseHeader, Passphrase);
            restoreRequest.AddFile("restore", encryptedPath, "application/octet-stream");

            var restoreResponse = RestClient.Execute(restoreRequest);
            restoreResponse.StatusCode.Should().Be(HttpStatusCode.OK, restoreResponse.Content);
        }
    }
}
