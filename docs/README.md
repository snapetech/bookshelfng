# Documentation index

Use the [main README](../README.md) for the project overview, quick start, and
metadata provider configuration.

## Product behavior and library workflows

| Guide | Covers |
| --- | --- |
| [Capabilities and compatibility](capabilities-and-compatibility.md) | Supported formats, metadata providers, compatibility limits, and BookshelfNG-specific changes |
| [Import matching and formats](import-matching-and-formats.md) | Coexisting ebook and audiobook files, import thresholds, weighted edition terms, and preferred series |
| [Moving existing media](media-storage.md) | Preview and move existing ebook or audiobook files using the web UI, API, or CLI |
| [Search and downloads](search-and-downloads.md) | Add-page search, indexer release search, existing-torrent adoption, and ignoring queue items |
| [Series pack search](series-pack-search.md) | Searching a series and reviewing multi-book packs in Manual Import |
| [Author metadata refresh](author-metadata-refresh.md) | Stored author metadata, refresh schedule, request limits, and troubleshooting |
| [Reporting issues](reporting-issues.md) | Which application and container logs to include for UI and API failures |

## Audiobooks and integrations

| Guide | Covers |
| --- | --- |
| [Audiobook metadata](audiobookshelf-metadata.md) | Narrator fields and tags, plus Audiobookshelf OPF sidecars |
| [Audiobook M4B merging](audiobook-m4b-merging.md) | Combining multi-track downloads into chaptered M4B files |
| [BookLore integration](booklore-integration.md) | Uploading imported files to BookLore BookDrop |

## Installation and release operations

| Guide | Covers |
| --- | --- |
| [Standalone installation](standalone-install.md) | Native archives, Windows installer, packages, and platform setup |
| [Distribution channels](distribution.md) | Published artifacts, release destinations, and publisher configuration |
| [.NET 10 platform builds](dotnet-10-platform-builds.md) | Linux x86 and FreeBSD runtime build details |
| [Release-note fragments](../release-notes/README.md) | User-facing release-note format and preview command |
| [Optional diagnostics module](../src/Bookshelf.Diagnostics/README.md) | Installation, configuration, and exported data |
