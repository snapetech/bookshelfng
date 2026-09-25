# BookshelfNG for YunoHost

This package runs BookshelfNG, an evolution of Readarr for ebooks and audiobooks. It carries forward Readarr's author monitoring and download automation while adding provider-selectable metadata, ISBN/ASIN-aware edition matching, wider file-format support, and integrations including Hardcover, MyAnonamouse, BookLore, and Calibre.

The package installs the project's checksummed, self-contained Linux release archives for `amd64` and `arm64`. YunoHost provides the service account, persistent data directory, port, and Nginx proxy. BookshelfNG receives its YunoHost path and port through supported server environment settings and binds only to `127.0.0.1`.

The `latest_github_release` rule follows stable BookshelfNG releases named `main-vMAJOR.MINOR.PATCH.BUILD` and selects the matching architecture archive. The YunoHost release updater can then propose changes to the package manifest; administrators apply package updates through YunoHost.

App configuration and databases live in the persistent data directory. Backups stop the service while copying the SQLite state. External book, audiobook, and download folders remain in place and need their own backup policy.
