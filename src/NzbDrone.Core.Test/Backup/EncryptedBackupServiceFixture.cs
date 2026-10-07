using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using NzbDrone.Core.Backup;

namespace NzbDrone.Core.Test.Backup
{
    [TestFixture]
    public class EncryptedBackupServiceFixture
    {
        private const string Passphrase = "bookshelf-backup-passphrase";

        private string _directory;
        private EncryptedBackupService _subject;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"bookshelf-encrypted-backup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            _subject = new EncryptedBackupService();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void should_roundtrip_multiple_authenticated_chunks()
        {
            var source = Path.Combine(_directory, "source.zip");
            var encrypted = Path.Combine(_directory, "backup.enc");
            var restored = Path.Combine(_directory, "restored.zip");
            var original = new byte[(1024 * 1024 * 2) + 37];
            RandomNumberGenerator.Fill(original);
            File.WriteAllBytes(source, original);

            _subject.Encrypt(source, encrypted, Passphrase);

            Assert.That(File.ReadAllBytes(encrypted), Is.Not.EqualTo(original));

            _subject.Decrypt(encrypted, restored, Passphrase);

            Assert.That(File.ReadAllBytes(restored), Is.EqualTo(original));
        }

        [Test]
        public void should_reject_wrong_passphrase_and_tampered_chunk()
        {
            var source = Path.Combine(_directory, "source.zip");
            var encrypted = Path.Combine(_directory, "backup.enc");
            var restored = Path.Combine(_directory, "restored.zip");
            File.WriteAllBytes(source, new byte[32]);
            _subject.Encrypt(source, encrypted, Passphrase);

            Assert.Throws<InvalidDataException>(() => _subject.Decrypt(encrypted, restored, "another-long-passphrase"));
            Assert.That(File.Exists(restored), Is.False);

            var bytes = File.ReadAllBytes(encrypted);
            bytes[^1] ^= 0x01;
            File.WriteAllBytes(encrypted, bytes);

            Assert.Throws<InvalidDataException>(() => _subject.Decrypt(encrypted, restored, Passphrase));
            Assert.That(File.Exists(restored), Is.False);
        }

        [Test]
        public void should_reject_truncated_envelope()
        {
            var encrypted = Path.Combine(_directory, "backup.enc");
            var restored = Path.Combine(_directory, "restored.zip");
            File.WriteAllBytes(encrypted, new byte[16]);

            Assert.Throws<InvalidDataException>(() => _subject.Decrypt(encrypted, restored, Passphrase));
            Assert.That(File.Exists(restored), Is.False);
        }

        [Test]
        public void should_reject_short_passphrases()
        {
            var source = Path.Combine(_directory, "source.zip");
            var encrypted = Path.Combine(_directory, "backup.enc");
            File.WriteAllBytes(source, new byte[1]);

            Assert.Throws<ArgumentException>(() => _subject.Encrypt(source, encrypted, "too-short"));
            Assert.That(File.Exists(encrypted), Is.False);
        }
    }
}
