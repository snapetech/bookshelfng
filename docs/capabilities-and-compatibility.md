# Capabilities and compatibility

This guide maps the Bookshelf and Readarr features BookshelfNG carries, the
changes maintained in this repository, and the boundaries that affect
metadata identity and deployment. It separates shared product behavior from
BookshelfNG-specific implementation so inherited features are not presented
as unique additions.

For upstream descriptions, see the [Bookshelf README](https://github.com/pennydreadful/bookshelf/blob/develop/README.md)
and [Readarr README](https://github.com/Readarr/Readarr/blob/develop/README.md).
Bookshelf presents itself as a revival of Readarr. Readarr's repository now
announces retirement, while its book-automation workflow remains the base
BookshelfNG maintains. This comparison reflects the linked README content
checked on 2026-09-23.

## Product scope

BookshelfNG is a complete, self-hosted ebook and audiobook manager. It can
monitor authors and books, search indexers for releases, send grabs to
download clients, import files, and manage library metadata without SeerrNG.
SeerrNG is an optional integration that can request books through the
Readarr-compatible API.

One instance supports one format per book. Run separate instances for ebooks
and audiobooks when both are needed. Persist `/config`; that volume contains
the database, application settings, and provider identities needed to keep an
existing library usable across container replacement.

## Supported file formats

BookshelfNG recognizes these file extensions for library scans and download
imports:

| Media type | File extensions |
| --- | --- |
| Ebook | `.azw3`, `.epub`, `.kepub`, `.mobi`, `.pdf` |
| Audiobook | `.aac`, `.alac`, `.ape`, `.flac`, `.m4a`, `.m4b`, `.m4p`, `.mp2`, `.mp3`, `.mp4a`, `.oga`, `.ogg`, `.vorbis`, `.wav`, `.wavpack`, `.wma` |

Some audio extensions share the same profile quality class. This table
describes files BookshelfNG can recognize; it does not imply that every format
stores all book metadata tags. See [media extension and quality mapping](../src/NzbDrone.Core/MediaFiles/MediaFileExtensions.cs)
and [ebook metadata reading](../src/NzbDrone.Core/MediaFiles/EbookTagService.cs).

## Metadata modes and provider identity

| Configuration | Primary metadata path | Suitable use | Identity constraint |
| --- | --- | --- | --- |
| `softcover` image | Goodreads-compatible metadata | Existing Readarr/softcover libraries and services | Stored Goodreads-compatible IDs stay tied to that provider. |
| `hardcover` image, default | BookshelfNG's native Hardcover GraphQL client | A direct Hardcover-backed library without a metadata proxy | Hardcover author, work, and edition IDs are distinct from Goodreads IDs. |
| `METADATA_URL` | A Readarr-compatible service, hosted or self-hosted | Keep an existing proxy or use a service that implements the compatible API | BookshelfNG expects that service to return stable IDs and resolvable detail records. |

In the `hardcover` image, set `HARDCOVER_NATIVE=false` to select the
`METADATA_URL` compatibility path. This is an explicit choice. An HTTP,
authentication, malformed-response, or rate-limit error from the primary
provider does not cause BookshelfNG to switch providers automatically.

Provider IDs are not interchangeable. A Goodreads work or edition ID, a
Hardcover ID, a Google Books volume ID, a LOC record, and an Open Library key
describe records in different catalogs. Changing an image tag or endpoint does
not convert database IDs. Back up the library and follow the
[Hardcover migration guidance](../README.md#moving-an-existing-library-to-hardcover)
before moving a Goodreads-compatible database to Hardcover.

## Supplemental runtime catalogs

The additional catalogs are parallel search sources. They do not replace the
selected primary metadata provider or merge their records into Hardcover.
BookshelfNG assigns each result a provider-qualified ID and routes later book
and author lookups back to the same source. The configured source and its
credentials must remain available while records from that source are in use.

| Catalog | Enablement | Detail lookup and limits |
| --- | --- | --- |
| Google Books | Set `GOOGLE_BOOKS_API_KEY`; enabled by default when the key is present. | Uses Google volume IDs. Search and detail responses are cached; the API key is required for later details. |
| Library of Congress | Enabled by default; public search. | Uses LOC record URLs. Requests are paced to one per 3.2 seconds per BookshelfNG process and successful responses are cached for one day. |
| Europeana | Set `EUROPEANA_API_KEY`; enabled by default when the key is present. | Searches open-reuse text records. Provider IDs resolve through the Europeana Record API. ISBN values in `dcIdentifier` are normalized by removing punctuation before they are mapped. It is a cultural-heritage catalog, so coverage and covers vary. |
| Goodreads-compatible Apify Actor | Opt in with `BOOKSHELF_METADATA_SOURCES=...`, an Actor name, and an Apify token. | Actor schema, availability, terms, and possible charges are controlled by the Actor publisher. Results use stable record keys where available. |

`BOOKSHELF_METADATA_SOURCES` accepts `googlebooks`, `loc`, `europeana`, and
`apify-goodreads`. When set, it replaces the default list; an empty value
disables supplemental sources. The setting applies to both `hardcover` and
`softcover` images. Provider failures are logged independently, so an
unavailable supplemental catalog does not suppress results from other
configured sources.

Search results are cached for ten minutes. Google Books, LOC, and Europeana
HTTP responses and Apify result sets are cached for one day. These are
process-level request caches, not durable metadata storage. Namespaced IDs
include `googlebooks:<volume-id>`, `loc:<encoded-record-url>`,
`europeana:<encoded-record-id>`, and
`apify-goodreads:<encoded-record-key>`.

Open Library is not an ordinary BookshelfNG search or detail provider. The
SeerrNG migration helper may use Open Library, Google Books, LOC, or an
optional Apify Actor to recover candidate metadata while it remaps records;
that work is separate from BookshelfNG's runtime metadata path. See the
[SeerrNG support matrix](https://github.com/Snapetech/seerrng/blob/main/docs/using-seerr/bookshelf-metadata-sources.md).

## Search, identifiers, and edition matching

When local file metadata provides identifiers, candidate search tries ISBN,
ASIN, and Goodreads book ID before falling back to names. Name search uses
author and title when both are available, then title alone. This lets BookshelfNG
search a title-only file, but it does not guarantee that an ambiguous result
can be selected automatically.

The ebook tag reader extracts ISBN and ASIN metadata from supported ebook
formats. For EPUB ISBN identifiers, it removes punctuation and other
non-identifier characters, validates ISBN-10 and ISBN-13 check digits, then
prefers a valid 978-prefixed ISBN-13, a valid 979-prefixed ISBN-13, and finally
another valid candidate. If multiple candidates have the same preference,
longer values are considered first. ASIN values are retained separately.

Candidate editions are ranked by author and title similarity, ISBN, ASIN,
publication year, language, publisher, and whether the catalog format fits an
ebook or audiobook file. ISBN and ASIN mismatches carry more weight than
missing identifiers. Series-title variants and common author-name formats are
also considered. BookshelfNG preserves provider search ordering so equally
ranked search results remain predictable. The importer can still need manual
review when file tags are sparse or several editions look alike.

When BookshelfNG creates the initial `Standard` metadata profile, it sets the
minimum popularity to 50 when `HARDCOVER=true` and 350 otherwise. The lower
Hardcover default can keep more low-popularity books in catalog refreshes.
This is only an initialization default: an existing metadata profile is not
rewritten. Books with a future release date also pass the popularity filter.

Europeana returns identifiers in forms such as `urn:isbn:978-3-16-148410-0`.
BookshelfNG removes non-digit characters and keeps values with a 13-digit
`978` or `979` ISBN shape before storing them as edition ISBNs. This
provider-specific mapping does not rewrite ISBNs or foreign IDs already stored
in the library.

Implementation references:

- [Ebook identifier extraction and ISBN checks](../src/NzbDrone.Core/MediaFiles/EbookTagService.cs)
- [Remote candidate searches by ISBN, ASIN, IDs, and names](../src/NzbDrone.Core/MediaFiles/BookImport/Identification/CandidateService.cs)
- [Edition candidate scoring](../src/NzbDrone.Core/MediaFiles/BookImport/Identification/DistanceCalculator.cs)
- [Candidate selection and local/remote matching](../src/NzbDrone.Core/MediaFiles/BookImport/Identification/IdentificationService.cs)
- [Supplemental metadata providers](../src/NzbDrone.Core/MetadataSource/BookInfo/AdditionalBookMetadataProxy.cs)
- [Provider selection, primary search, and supplemental search merge](../src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs)
- [Native Hardcover GraphQL client](../src/NzbDrone.Core/MetadataSource/Hardcover/HardcoverMetadataProxy.cs)

## Author data, request efficiency, and refresh

Author and book records are durable application database entities. During a
book refresh, if the returned author has the same provider ID as an author
already in the database, BookshelfNG reuses that author and its metadata
record instead of fetching the author's full book catalog again. Missing
authors are fetched so the book keeps a valid parent record.

Automatic author refresh checks stored `LastInfoSync` data: never-synced or
over-30-day-old records are due; a successful update within 12 hours suppresses
another automatic refresh; continuing authors become due after two days; and
an author with a book released within 30 days is refreshed. Manual refresh and
metadata-profile changes can force work past the age check. Failures do not
make old data appear fresh. See the detailed
[author metadata storage and refresh guide](author-metadata-refresh.md) for
the decision order, logs, failure behavior, and source references.

Native Hardcover responses also have shorter process-local caches: author
details for 30 minutes, works for six hours, and searches for ten minutes.
Text search batches result IDs into one book detail query; ISBN/ASIN and
edition lookups fetch the parent work in the same GraphQL operation. Requests
are paced to one per second per process. The client honors `Retry-After` and
exhausted rate-limit reset headers, pauses requests until the reset, and does
not retry a 429 into the limit. Separate BookshelfNG processes do not share
their cache or coordinate their pacing.

## Imports and integrations

BookshelfNG carries the Readarr-style library workflow and supports the
following import and service connections:

- Goodreads-compatible bookshelves, owned books, series, and public lists.
- Native Hardcover list imports with the selected list IDs saved in the
  import-list configuration.
- A native MyAnonamouse indexer with search filters and tracker session
  configuration.
- qBittorrent 5.2 bearer API-key authentication, alongside inherited
  qBittorrent authentication and other download clients.
- A Readarr-compatible API. `/api/v1/book/lookup` includes nested author and
  edition records when the provider can resolve them, which lets SeerrNG
  consume useful metadata in a request flow.
- Optional chaptered M4B generation for multi-track audiobook downloads; see
  [Audiobook M4B merging](#audiobook-m4b-merging).

Relevant implementation:

- [Hardcover list import and selected lists](../src/NzbDrone.Core/ImportLists/Hardcover/HardcoverImport.cs)
- [Goodreads list import implementations](../src/NzbDrone.Core/ImportLists/Goodreads)
- [MyAnonamouse indexer](../src/NzbDrone.Core/Indexers/MyAnonaMouse/MyAnonaMouse.cs)
- [qBittorrent v2 API authentication](../src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentProxyV2.cs)
- [Book lookup API response](../src/Readarr.Api.V1/Books/BookLookupController.cs)

## Audiobook M4B merging

M4B merging is disabled by default. Set `BOOKSHELF_M4B_MERGE=true` to combine
approved multi-track audiobook downloads into a chaptered file. The standard
Docker image includes FFmpeg. See the dedicated [M4B merging guide](audiobook-m4b-merging.md)
for track ordering, conversion settings, source-file handling, native install
configuration, and implementation references.

## Privacy and optional diagnostics

The standard app image contains no Servarr Sentry integration, diagnostics
reporter, or reporting SDK. The optional diagnostics module is a separately
built assembly that must be copied into `/config/plugins`, explicitly enabled,
and configured with an HTTPS OTLP endpoint and bearer token. It exports
aggregated application-start, book-grab, book-import, and download-failure
counts every 15 minutes, along with the BookshelfNG service name and version.
It sends no book or author details, paths, search terms, logs, exception
details, or persistent installation ID. The configured collector can observe
the network request's source IP. See the [diagnostics installation and data
schema](../src/Bookshelf.Diagnostics/README.md).

## Significant work maintained in BookshelfNG

This is a feature-level inventory of the main changes since BookshelfNG began
publishing its own images. It groups related commits rather than listing
dependency updates and routine maintenance individually.

| Period | Maintained work |
| --- | --- |
| 2025 | Updated Linux container packaging and multi-architecture publishing for amd64 and arm64, adopted the LinuxServer entrypoint, added build caching and GHCR image publication, set a lower initial metadata-profile popularity threshold when `HARDCOVER=true`, added configurable self-hosted metadata endpoints and native MyAnonamouse support, preserved search ordering, and removed Servarr Sentry reporting. The current Bookshelf README also documents several of these capabilities. |
| January 2026 | Added native Hardcover import lists and made the import sync only the list IDs selected in configuration. |
| May–July 2026 | Enriched `/api/v1/book/lookup` with author and edition metadata, added tagged image publishing and downstream edge builds, and added qBittorrent 5.2 bearer API-key authentication. |
| August 2026 | Added a native Hardcover GraphQL metadata client so the Hardcover image can search and resolve records directly without a metadata proxy. |
| September 2026 | Added title-only and normalized-filename search for imports without author metadata; added optional chaptered M4B generation for multi-track audiobook downloads; added runtime Google Books, LOC, Europeana, and optional Apify catalogs with provider-qualified identities; batched Hardcover lookups and honored rate-limit resets; reused stored author records and introduced periodic freshness checks; added a separately installed diagnostics module; and added curated GitHub releases and Discord build announcements. |

BookshelfNG retains ebook identification behavior from the Readarr lineage:
EPUB ISBN normalization and checksum checks, ISBN-13 preference, ASIN matching,
edition-aware scoring, and author/title search. The ISBN reader and core
release scoring predate this fork. BookshelfNG added title-only and normalized
filename search in September 2026 for imports without author metadata; this
search fallback does not guarantee that an ambiguous candidate is safe to
select automatically. Europeana's ISBN punctuation removal is part of its
newer provider mapping.

## What is shared with upstream

The current upstream Bookshelf README already describes its Readarr revival,
one-format-per-instance model, Goodreads and Hardcover modes, native
MyAnonamouse support, Hardcover list imports, self-hosted metadata options,
improved matching, and removal of Servarr analytics. These are Bookshelf
lineage capabilities and should not be described as unique to BookshelfNG.
BookshelfNG maintains and extends that base; its notable implementation work
is the direct Hardcover GraphQL client and request controls, supplemental
runtime catalogs and stable IDs, durable author refresh policy, optional
diagnostics module, richer book lookup API response, qBittorrent 5.2
authentication, and current image/release automation.

BookshelfNG also retains workflow features from Readarr that the brief
Bookshelf README does not enumerate: existing-library scans, quality-based
upgrades and renaming, interactive release search for compatible indexers,
configurable re-search after failed downloads, and Calibre Content Server
integration for adding books and converting ebook formats. These are part of
the inherited application workflow, not BookshelfNG-specific additions. See
the [interactive search API](../src/Readarr.Api.V1/Indexers/ReleaseController.cs),
[failed-download re-search handler](../src/NzbDrone.Core/Download/RedownloadFailedDownloadService.cs),
[Calibre Content Server client](../src/NzbDrone.Core/Books/Calibre/CalibreProxy.cs),
and [library upgrade path](../src/NzbDrone.Core/MediaFiles/UpgradeMediaFileService.cs).

The upstream READMEs do not capture every compatibility detail in the running
code. In particular, BookshelfNG's direct Hardcover client, provider-specific
detail routes, migration boundary, author reuse rules, optional-module
installation, and current rate-limit behavior are documented in this guide
and the linked operator docs.

## Release and deployment workflow

The repository publishes `softcover` and `hardcover` container images, with
rolling and versioned tags. Tagged `main-v*` builds publish curated GitHub
Release notes and announce the successful image build to Discord. User-facing
changes require a release-note fragment; internal-only changes use the
documented opt-out. See the [release-note format](../release-notes/README.md)
and the [README release overview](../README.md#releases).

Images use `/config` for durable application state. Mount library and download
paths so both BookshelfNG and its download clients can read and write the
expected files. The Docker Compose example and tag format are in the [main
README](../README.md#quick-start).
