using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Backup;
using NzbDrone.Http.REST.Attributes;
using Readarr.Http;
using Readarr.Http.REST;

namespace Readarr.Api.V1.System.Backup
{
    [V1ApiController("system/backup")]
    public class BackupController : Controller
    {
        private readonly IBackupService _backupService;
        private readonly IEncryptedBackupService _encryptedBackupService;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;

        private static readonly List<string> ValidExtensions = new () { ".zip", ".db", ".xml", ".enc" };

        public BackupController(IBackupService backupService,
                            IEncryptedBackupService encryptedBackupService,
                            IAppFolderInfo appFolderInfo,
                            IDiskProvider diskProvider)
        {
            _backupService = backupService;
            _encryptedBackupService = encryptedBackupService;
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
        }

        [HttpGet]
        public List<BackupResource> GetBackupFiles()
        {
            var backups = _backupService.GetBackups();

            return backups.Select(b => new BackupResource
                {
                    Id = GetBackupId(b),
                    Name = b.Name,
                    Path = $"/backup/{b.Type.ToString().ToLower()}/{b.Name}",
                    Type = b.Type,
                    Size = b.Size,
                    Time = b.Time
                })
                .OrderByDescending(b => b.Time)
                .ToList();
        }

        [RestDeleteById]
        public object DeleteBackup(int id)
        {
            var backup = GetBackup(id);

            if (backup == null)
            {
                throw new NotFoundException();
            }

            var path = GetBackupPath(backup);

            if (!_diskProvider.FileExists(path))
            {
                throw new NotFoundException();
            }

            _diskProvider.DeleteFile(path);

            return new { };
        }

        [HttpPost("restore/{id:int}")]
        public object Restore(int id)
        {
            var backup = GetBackup(id);

            if (backup == null)
            {
                throw new NotFoundException();
            }

            var path = GetBackupPath(backup);

            _backupService.Restore(path);

            return new
            {
                RestartRequired = true
            };
        }

        [HttpPost("encrypted")]
        public IActionResult CreateEncryptedBackup()
        {
            var passphrase = GetPassphrase();
            var encryptedPath = Path.Combine(_appFolderInfo.TempFolder, $"bookshelf_backup_{Guid.NewGuid():N}.enc");
            string backupPath = null;

            try
            {
                backupPath = _backupService.CreateTemporaryBackup();
                _encryptedBackupService.Encrypt(backupPath, encryptedPath, passphrase);

                var stream = global::System.IO.File.OpenRead(encryptedPath);
                Response.Headers.CacheControl = "no-store, no-cache";
                Response.Headers.Pragma = "no-cache";
                Response.OnCompleted(() =>
                {
                    DeleteTemporaryFile(backupPath);
                    DeleteTemporaryFile(encryptedPath);
                    return Task.CompletedTask;
                });

                return File(stream, "application/octet-stream", $"bookshelfng_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.enc");
            }
            catch
            {
                DeleteTemporaryFile(backupPath);
                DeleteTemporaryFile(encryptedPath);
                throw;
            }
        }

        [HttpPost("restore/upload")]
        [RequestSizeLimit(1000000000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 1000000000)]
        public object UploadAndRestore()
        {
            var files = Request.Form.Files;

            if (files.Empty())
            {
                throw new BadRequestException("file must be provided");
            }

            var file = files.First();
            var extension = Path.GetExtension(file.FileName) ?? string.Empty;

            if (!ValidExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw new UnsupportedMediaTypeException($"Invalid extension, must be one of: {ValidExtensions.Join(", ")}");
            }

            var path = Path.Combine(_appFolderInfo.TempFolder, $"readarr_backup_restore_{Guid.NewGuid():N}{extension}");
            var decryptedPath = Path.Combine(_appFolderInfo.TempFolder, $"readarr_backup_restore_{Guid.NewGuid():N}.zip");

            try
            {
                _diskProvider.SaveStream(file.OpenReadStream(), path);

                if (extension.Equals(".enc", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        _encryptedBackupService.Decrypt(path, decryptedPath, GetPassphrase());
                    }
                    catch (InvalidDataException exception)
                    {
                        throw new BadRequestException(exception.Message);
                    }

                    _backupService.Restore(decryptedPath);
                }
                else
                {
                    _backupService.Restore(path);
                }
            }
            finally
            {
                DeleteTemporaryFile(path);
                DeleteTemporaryFile(decryptedPath);
            }

            return new
            {
                RestartRequired = true
            };
        }

        private string GetBackupPath(NzbDrone.Core.Backup.Backup backup)
        {
            return Path.Combine(_backupService.GetBackupFolder(backup.Type), backup.Name);
        }

        private int GetBackupId(NzbDrone.Core.Backup.Backup backup)
        {
            return HashConverter.GetHashInt31($"backup-{backup.Type}-{backup.Name}");
        }

        private NzbDrone.Core.Backup.Backup GetBackup(int id)
        {
            return _backupService.GetBackups().SingleOrDefault(b => GetBackupId(b) == id);
        }

        private string GetPassphrase()
        {
            var passphrase = Request.Headers[EncryptedBackupService.PassphraseHeader].ToString();

            if (!EncryptedBackupService.IsValidPassphrase(passphrase))
            {
                throw new BadRequestException($"A passphrase with at least {EncryptedBackupService.MinimumPassphraseLength} characters is required.");
            }

            return passphrase;
        }

        private void DeleteTemporaryFile(string path)
        {
            if (!string.IsNullOrEmpty(path) && _diskProvider.FileExists(path))
            {
                _diskProvider.DeleteFile(path);
            }
        }
    }
}
