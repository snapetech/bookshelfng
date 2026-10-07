# Encrypted portable backups

The Backups page can create an encrypted portable archive containing the
configuration and SQLite database. The archive is downloaded as a `.enc` file
and is not added to the server's ordinary backup folder. The temporary archive
and encrypted response file are removed after the download finishes.

Choose a passphrase with at least 16 characters and keep it separately from the
backup. BookshelfNG cannot recover a lost passphrase. To restore, choose the
`.enc` file in **System → Backup → Restore Backup** and enter its passphrase.
Existing `.zip`, `.db`, and `.xml` restores continue to work.

Encrypted exports use AES-256-GCM in authenticated 1 MiB chunks, with
PBKDF2-HMAC-SHA256 at 600,000 iterations. Each file has a random salt and nonce
prefix. Restore rejects a wrong passphrase, truncated file, or modified chunk
before BookshelfNG reads the decrypted archive. The plaintext archive is
limited to 900,000,000 bytes; restore uploads retain the existing 1,000,000,000
byte request limit.

The authenticated API is:

- `POST /api/v1/system/backup/encrypted`, with the API key and
  `X-Bookshelf-Backup-Passphrase` headers, to download an encrypted backup.
- `POST /api/v1/system/backup/restore/upload`, with a multipart `restore` file
  and the same passphrase header when the filename ends in `.enc`.

Send the passphrase in a header over HTTPS when using the API remotely. The
application must receive the passphrase to encrypt or restore a file, so the
application host and its administrators remain inside the trust boundary.
