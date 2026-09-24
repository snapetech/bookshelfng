# BookshelfNG

BookshelfNG is a self-hosted ebook and audiobook library manager descended
from Readarr. Follow authors and books, monitor indexers for new releases,
automatically grab downloads, and organize, rename, and upgrade files in your
library. BookshelfNG is a complete app on its own. It does not require SeerrNG
or a metadata proxy.

BookshelfNG is for people who want Readarr-style book automation with a choice
of metadata providers. Goodreads-compatible metadata services and native
Hardcover remain available for primary library refreshes. Runtime catalog
search is independently selectable across those providers, Open Library, and
the other public catalogs. See [Metadata sources](#metadata-sources) for
provider behavior and settings.

## Capabilities

- **Choose how book metadata is served.** The `hardcover` image includes a
  native Hardcover GraphQL provider. Search, author, work, edition, series,
  ISBN, and ASIN lookups go from BookshelfNG directly to Hardcover. There is
  no proxy between them. When you need one, BookshelfNG can use a
  Readarr-compatible metadata endpoint instead, including a service you host
  yourself.
- **Keep old libraries usable.** The `softcover` image retains Goodreads
  compatible IDs and import behavior for existing Readarr/softcover databases.
  You can also choose a compatible metadata endpoint with `METADATA_URL`.
- **Identify ebooks from their own metadata.** BookshelfNG reads identifiers
  embedded in ebook files, strips ISBN formatting, validates ISBN-10 and
  ISBN-13 check digits, and prefers ISBN-13 when an ebook provides multiple
  valid identifiers. ISBN and ASIN are also used for direct metadata searches
  and edition matching.
- **Find and compare likely releases.** Import identification searches by
  ISBN, ASIN, and Goodreads ID before using author and title. If author
  information is missing, it can search by title alone. Candidate editions
  are ranked using identifiers and available edition details; ambiguous
  matches can still need review. Search result ordering is preserved.
- **Import common ebook and audiobook formats.** Recognized ebooks include
  EPUB, KEPUB, MOBI, AZW3, and PDF. Audiobooks include M4B, MP3, FLAC, AAC,
  M4A, OGG, and related formats; see the [complete extension list](docs/capabilities-and-compatibility.md#supported-file-formats).
- **Import books from the lists you use.** BookshelfNG supports Goodreads
  shelves, owned books, series, and Listopia lists, plus native Hardcover list
  imports. Hardcover imports honor the lists selected in configuration.
- **Search MyAnonamouse directly.** The native MyAnonamouse indexer lets you
  use the tracker without adding Prowlarr as an intermediary.
- **Connect current download clients.** BookshelfNG supports qBittorrent 5.2
  authentication alongside the download clients inherited from Readarr.
- **Build chaptered audiobook files when needed.** Completed downloads with
  multiple tracks for one edition can be merged into a single M4B when
  `BOOKSHELF_M4B_MERGE=true` and an FFmpeg executable is available. This is
  opt-in; the standard Docker image includes FFmpeg.
- **Keep the familiar automation.** Monitor authors and books, watch indexer
  RSS feeds for releases, search for missing monitored books, apply quality
  and metadata profiles, manage download clients, scan and import existing
  files, and rename or upgrade releases automatically.
- **Search and recover downloads.** Use interactive release search to choose
  an available release when an indexer supports it. BookshelfNG can search
  again after a failed download; automatic grabs and manually selected grabs
  have separate re-search settings.
- **Connect a Calibre Content Server.** Configure a Calibre library as a root
  folder to add imported books to the library, sync metadata, and convert into
  configured output formats.
- **Keep library activity private.** BookshelfNG removes Servarr's Sentry
  analytics and exception-reporting integration. An independent diagnostics
  module is available for operators who explicitly install and enable it; the
  standard app image contains no reporting module or telemetry SDK.

BookshelfNG retains the Readarr-compatible API and the broader Readarr
library-management workflow. It supports one format per book in an instance;
run separate ebook and audiobook instances if you want both formats of the
same title.

Some capabilities are part of the Bookshelf and Readarr lineage rather than
unique BookshelfNG additions. The [capabilities and compatibility guide](docs/capabilities-and-compatibility.md)
compares those upstream features with BookshelfNG's maintained changes and
documents provider boundaries, migration limits, and implementation links.

Author and book metadata are persisted in BookshelfNG's application database.
Refreshing a book for an author already in the library reuses that local author
record when the provider IDs match, so a book refresh does not need to fetch the
author's full catalog again. Automatic author validation is age and activity
based; manual refreshes and metadata-profile changes can force an update. See
[Author metadata storage and refresh behavior](docs/author-metadata-refresh.md)
for the exact rules, Hardcover request caching and rate-limit behavior, failure
handling, troubleshooting logs, and source-code references.

## Quick start

The web interface listens on port `8787`. Persist `/config`, and mount your
downloads and library paths so BookshelfNG and your download clients can
access them.

```yaml
services:
  bookshelf:
    image: ghcr.io/snapetech/bookshelfng:hardcover
    ports:
      - "8787:8787"
    volumes:
      - ./bookshelf-config:/config
      - /path/to/downloads:/downloads
      - /path/to/books:/books
    restart: unless-stopped
```

Start it with `docker compose up -d`, then open `http://localhost:8787` to set
up your root folder, metadata source, indexers, and download clients. The
`softcover` image is available for Goodreads-compatible libraries. Both
published Linux images support `amd64` and `arm64` hosts.

## Standalone app

BookshelfNG also runs directly on Windows, macOS, Linux, and FreeBSD. Native
release archives include the .NET runtime and web UI; they do not require
SeerrNG or a container runtime. Start an extracted Linux, macOS, or FreeBSD
archive with:

```sh
./Readarr -nobrowser -data="$HOME/.local/share/bookshelfng"
```

On Windows, run `Readarr.exe` and set a persistent data path under
`%LOCALAPPDATA%`. Then open `http://localhost:8787`. The
[standalone installation guide](docs/standalone-install.md) covers platform
archives, data migration, system services, and package-specific setup.

The release pipeline builds `.deb`, RPM, Snap, Flatpak bundle, AppImage,
Chocolatey, and Helm artifacts, and supports AUR, COPR, and Ubuntu PPA
publishing when those publisher accounts are connected. See the
[distribution channels guide](docs/distribution.md) for package links,
repository setup, and release details.

## Optional audiobook M4B merging

To combine a multi-track audiobook download into a single chaptered M4B, set
`BOOKSHELF_M4B_MERGE=true`. This applies to completed downloads received from a
download client. BookshelfNG groups approved tracks by book and edition,
orders them using embedded disc and track numbers or filenames, creates a
chapter for each track, and reruns the normal import checks on the merged
file. If FFmpeg is unavailable, a merge fails, or the merged file fails import
checks, BookshelfNG keeps the original tracks and continues the normal import.

The standard Docker image includes FFmpeg. For native installs, provide an
executable named `ffmpeg` on BookshelfNG's `PATH`, or set
`BOOKSHELF_FFMPEG_PATH` to its path. The merge transcodes audio to stereo AAC
at 44.1 kHz and 128 kbps by default.
Set `BOOKSHELF_M4B_AAC_BITRATE_KBPS` to a value from 48 to 320 to choose a
different bitrate. When the merged file imports successfully in move mode,
the original tracks are removed; copy mode and downloads that cannot be moved
retain their source tracks. See the [capabilities and compatibility guide](docs/capabilities-and-compatibility.md#audiobook-m4b-merging)
for the exact behavior and implementation references.

## Metadata sources

### Hardcover: direct API access

The `hardcover` image uses BookshelfNG's native Hardcover GraphQL provider by
default. It searches Hardcover for books and authors and maps work, edition,
series, ISBN, and ASIN data into Bookshelf's existing metadata model. This
removes the extra metadata service from the normal request path.

Native access requires a Hardcover API token at runtime:

```env
HARDCOVER=true
HARDCOVER_AUTH=Bearer your-hardcover-api-token
```

`HARDCOVER_API_KEY` is accepted in place of `HARDCOVER_AUTH`. The default API
endpoint is `https://api.hardcover.app`; set `HARDCOVER_API_URL` to use a
compatible endpoint or test service. The Hardcover token can also be entered
in **Settings > Metadata > Primary Metadata Provider**. Environment credentials
take precedence over the saved setting; the settings API never returns the
saved token.

Native mode handles transient request failures and sparse or nullable metadata
responses, maps Hardcover work and edition records into Bookshelf's library
model, and caches responses in the BookshelfNG process for the lifetime of the
instance. Author responses are cached for 30 minutes, work responses for six
hours, and search responses for 10 minutes. These response caches are separate
from the author and book metadata stored in the library database. See the
[Hardcover API getting-started guide](https://github.com/hardcoverapp/hardcover-docs/blob/main/src/content/docs/api/Getting-Started.mdx)
and [Hardcover author schema](https://github.com/hardcoverapp/hardcover-docs/blob/main/src/content/docs/api/GraphQL/Schemas/Authors.mdx)
for Hardcover's API reference. BookshelfNG does not silently switch to another
metadata service after an API error.

When BookshelfNG creates the initial `Standard` metadata profile on a database
with no profiles, `HARDCOVER=true` selects a minimum popularity of 50; other
modes use 350. This lower default can keep more low-popularity catalog books
during refresh. Existing profiles are preserved and can be adjusted in the
metadata profile settings.

Text search fetches Hardcover's result IDs in one batched book query instead
of making a separate detail request for every result. ISBN/ASIN searches and
edition lookups fetch the parent work in the same GraphQL operation. API
requests are paced at one per second. BookshelfNG honors Hardcover's
`Retry-After` and exhausted rate-limit headers; when a limit is reached, it
stops sending further requests from this process until the reported reset and
returns an error for calls made during that cooldown instead of retrying into
the limit.

### Selectable runtime catalogs

Every provider used for runtime book discovery can be turned on or off
independently in **Settings > Metadata > Runtime Catalog Sources**. This
selection controls title, ISBN, ASIN, and import searches. It does not change
how existing library records refresh. Results from an alternate catalog retain
a provider-qualified ID and continue to resolve through that provider. Results
from the configured primary catalog keep its existing ID format so current
libraries continue to match them; older unqualified IDs also keep using the
configured primary provider.

The selectable sources are Hardcover, the configured Readarr-compatible
metadata API, Open Library, Google Books, Library of Congress, Gutendex,
Internet Archive, NDL Search, Europeana, and the Goodreads-compatible Apify
adapter. With no saved source selection, Bookshelf enables LOC, Gutendex, and
the currently configured primary provider. Open Library, Internet Archive,
NDL Search, and Apify are opt-in. Google Books and Europeana are enabled when
their keys are set. Hardcover needs its API token. All sources can be disabled,
including the configured primary provider's catalog search.

`BOOKSHELF_METADATA_SOURCES` accepts `hardcover`, `metadata-api`,
`openlibrary`, `googlebooks`, `loc`, `gutendex`, `internetarchive`, `ndl`,
`europeana`, and `apify-goodreads`. A custom value replaces the saved UI
selection; an empty value disables all runtime catalog searches. The
environment setting remains authoritative and disables catalog selection in
the UI. Saved credentials are stored in the BookshelfNG configuration database
and returned as presence flags only. Non-empty credential environment values
take precedence over saved values.

```env
HARDCOVER=true
HARDCOVER_AUTH=Bearer your-hardcover-api-token
GOOGLE_BOOKS_API_KEY=your-google-books-api-key
EUROPEANA_API_KEY=your-europeana-api-key
```

With these keys, Google Books and Europeana results are also queried alongside
the other selected sources. Library of Congress and Gutendex run without keys. Gutendex covers
Project Gutenberg's literature catalog, including multiple languages, and is
not a current commercial-book catalog. Google Books and Europeana are skipped
when their keys are absent. To keep only the default public catalogs, set
`BOOKSHELF_METADATA_SOURCES=loc,gutendex,openlibrary`; to disable all catalog
searches, set
`BOOKSHELF_METADATA_SOURCES=`.

Open Library can be enabled in **Settings > Metadata > Runtime Catalog
Sources**. It uses its public Search, Work, Edition, and Author APIs. Requests
identify BookshelfNG with its User-Agent, are paced to one per second, and are
cached for one day. Add a contact email in the same settings page or set
`OPEN_LIBRARY_CONTACT_EMAIL` so Open Library can identify the installation.
Open Library Work IDs and Author IDs stay namespaced in BookshelfNG, and its
edition mapping validates and normalizes ISBN-10 values to ISBN-13. Covers are
loaded from Open Library's Covers API when available. Read Open Library's
[API usage policy](https://openlibrary.org/developers/api),
[Search API](https://openlibrary.org/dev/docs/api/search), and
[Covers API](https://openlibrary.org/dev/docs/api/covers) documentation.

Internet Archive searches its text collection and resolves records by stable
item identifier. Catalog records and edition metadata vary, so treat it as an
optional fallback. NDL Search adds Japanese and partner-library metadata; it
does not provide artwork because NDL ended its thumbnail service on March 31,
2026. It is opt-in because requests are paced to one per second and usage
conditions vary by provider. Operators must show NDL Search API credit and
check the provider's licensing/application requirements, especially for
commercial or continuous use. See NDL's [thumbnail service notice](https://ndlsearch.ndl.go.jp/news/20260401_thumbnail).

To add a Goodreads-compatible Apify Actor:

```env
# Include only the providers you want; use metadata-api instead of hardcover
# when the Readarr-compatible metadata API should be searched.
BOOKSHELF_METADATA_SOURCES=hardcover,googlebooks,loc,gutendex,internetarchive,ndl,europeana,apify-goodreads
HARDCOVER_APIFY_GOODREADS_ACTOR=publisher~goodreads-scraper
HARDCOVER_APIFY_TOKEN=your-apify-token
# Override only when the Actor does not use searchQueries and maxItems:
HARDCOVER_APIFY_GOODREADS_INPUT_TEMPLATE={"searchQueries":[{{query}}],"maxItems":10}
```

Google Books public searches require a Google API key; user OAuth is not
needed. Europeana requires a free key from a registered account and searches
text records with open reuse status. It is a cultural heritage index rather
than a complete current-book catalog, and per-record metadata and cover
availability vary. See Europeana's [Search API documentation](https://europeana.atlassian.net/wiki/spaces/EF/pages/2385739812/Search+API+Documentation),
[Record API documentation](https://europeana.atlassian.net/wiki/spaces/EF/pages/2385674279/Record+API+Documentation),
and [API key registration](https://pro.europeana.eu/page/get-api). Library of
Congress search is public and rate limited. Requests are paced to one per 3.2
seconds per Bookshelf process and successful responses are cached for one day.
If multiple Bookshelf processes share an outbound IP, set a source list that
avoids querying LOC from every process. The optional Apify adapter runs a
user-selected Actor, whose schema, availability, terms, and pricing are
controlled by its publisher. The default Actor input is
`{"searchQueries":[{{query}}],"maxItems":10}`; a custom JSON template must
include the literal `{{query}}` placeholder, which Bookshelf replaces with a
JSON-escaped search string. Actor output is normalized from common Goodreads
scraper fields, and results missing a title or author are skipped.

Results from alternate providers receive a namespaced foreign ID such as
`hardcover:123`, `metadata-api:123`, `openlibrary:OL...W`,
`googlebooks:volume-id`, `loc:<encoded-record-url>`,
`europeana:<encoded-record-id>`, or `apify-goodreads:<encoded-record-key>`.
Bookshelf uses those same IDs for subsequent book and author metadata lookups;
it does not coerce them into numeric Goodreads IDs. The selected provider
names and credentials must remain
available when the library is refreshed. Search results are cached for 10
minutes; Open Library, Google Books, LOC, and Europeana responses and Apify
result sets are cached for one day. Provider failures are logged independently;
other configured sources continue to return results.

The source checkboxes and optional credentials are managed separately by each
BookshelfNG instance. For an ebook/audiobook pair, configure the two instances
individually in their own **Settings > Metadata** pages. `HARDCOVER_NATIVE`,
`METADATA_URL`, and `HARDCOVER_API_URL` still select deployment mode or an API
endpoint and remain environment-level settings. SeerrNG's **Settings >
Metadata** page explains this boundary; its TMDB/TVDB selectors control video
metadata, not BookshelfNG catalogs. One-off SeerrNG migration-recovery adapters
also keep their own environment settings because they run outside BookshelfNG's
normal search process.

The source setting applies to the `hardcover` and `softcover` images. It does
not rewrite IDs already stored in the library, and it does not combine remote
catalog records into Hardcover itself.

### Goodreads-compatible metadata and proxies

The `softcover` image is intended for Goodreads-compatible metadata and
existing Readarr/softcover databases. Goodreads and Hardcover use different
foreign author, book, and edition IDs; those IDs cannot be converted by
changing an image tag or metadata URL.

BookshelfNG also supports the Readarr-compatible metadata API through
`METADATA_URL`. Use a hosted service such as rreading-glasses, preserve an
existing proxy or cache, or run your own compatible endpoint. In the
`hardcover` image, set `HARDCOVER_NATIVE=false` to select this compatibility
path instead of direct Hardcover GraphQL access. The choice is explicit, so
you can see which service is handling metadata requests.

`METADATA_URL` is a compatibility boundary, not an automatic multi-provider
aggregator. It can point to a service that itself translates or combines
providers, but BookshelfNG expects that service to return stable IDs and
compatible detail responses. Goodreads work and edition IDs, Hardcover IDs,
Google Books volume IDs, Open Library keys, and LOC identifiers are not
interchangeable. The optional runtime catalogs documented above provide
parallel search results and provider-specific detail lookups; they do not
rewrite IDs already stored in the library or silently replace a failed primary
provider.

## Moving an existing library to Hardcover

Do not switch an existing Goodreads/softcover database to Hardcover by changing
only the image tag or `METADATA_URL`. The two providers' foreign IDs are not
portable, and a direct switch can leave existing authors, books, and editions
that Hardcover cannot resolve.

Back up the ebook and audiobook configuration directories before migrating.
SeerrNG includes a migration helper that can inventory the source database,
rebuild strict matches against a temporary Hardcover BookshelfNG target, and
optionally use a softcover endpoint to recover metadata for stale IDs. Its
deterministic local database fallback can preserve books that Hardcover still
cannot import. Those fallback entries, such as `local:ebook:1076`, are local
Bookshelf records rather than native Hardcover records.

SeerrNG's migration helper can additionally search Google Books and the
Library of Congress and can optionally call a user-selected Apify Actor that
scrapes Goodreads-compatible catalogs. These sources are used to recover
candidate metadata and attempt strict remapping to Hardcover. Unmatched
metadata can enrich the optional local fallback. This tooling does not add
Google Books, LOC, or Apify results to BookshelfNG's ordinary search/detail
flows. Apify Actors are third-party services with Actor-specific schemas and
potential charges; Goodreads does not issue new public developer API keys.

## BookshelfNG with SeerrNG

SeerrNG can connect to BookshelfNG as a Readarr-compatible ebook or audiobook
service. That integration is optional; BookshelfNG can monitor, download, and
manage a library without SeerrNG.

For compatible book-add requests, BookshelfNG's
`/api/v1/book/lookup` response includes nested author and edition metadata when
it can resolve the requested book. Use the `softcover` image when retaining a
Readarr-compatible Goodreads database, or use the Hardcover migration process
above when moving to Hardcover metadata.

## Releases

GitHub Actions publishes these rolling and versioned container tags:

- `softcover`, `softcover-v0.4.20`, `softcover-v0.4.20.<run-number>`
- `hardcover`, `hardcover-v0.4.20`, `hardcover-v0.4.20.<run-number>`

Tagged `main-v*` builds also publish a GitHub Release with curated release
notes, platform archives, checksums, and native Linux packages, then announce
the release to Discord. AUR, COPR, PPA, Snap Store, Chocolatey, and Helm
publication are wired to the same verified tag flow; external stores require
their publisher credentials and repository registration. The Flatpak is
attached as a GitHub Release bundle and is not published to Flathub. See the
[distribution setup and publisher requirements](docs/distribution.md) for
channel details. Pull requests require a release-note fragment for user-facing
changes; internal-only work must be marked `release-note: none`. See
[`release-notes/README.md`](./release-notes/README.md) for the format and
preview command.

If an anonymous pull returns `denied`, the GHCR package may not be public yet;
you can also authenticate with package read access.

## Documentation

- [Capabilities, upstream comparison, and compatibility boundaries](docs/capabilities-and-compatibility.md)
- [Optional audiobook M4B merging](docs/audiobook-m4b-merging.md)
- [Metadata providers and configuration](#metadata-sources)
- [Moving an existing library to Hardcover](#moving-an-existing-library-to-hardcover)
- [Author metadata storage, refresh policy, and troubleshooting](docs/author-metadata-refresh.md)
- [.NET 10 platform builds for Linux x86 and FreeBSD](docs/dotnet-10-platform-builds.md)
- [Standalone installation and platform support](docs/standalone-install.md)
- [Release distribution channels and publisher setup](docs/distribution.md)
- [Optional diagnostics module](src/Bookshelf.Diagnostics/README.md)
- [Release-note format and preview](release-notes/README.md)
- [SeerrNG migration metadata source matrix](https://github.com/Snapetech/seerrng/blob/main/docs/using-seerr/bookshelf-metadata-sources.md)

## Support and contributing

Please file a GitHub issue or start a discussion for help. Contributions are
welcome, especially fixes and quality-of-life improvements. Current areas of
interest include monitoring series and supporting ebook and audiobook files
in the same root folder.

## Optional diagnostics

The standard BookshelfNG image sends no analytics or exception reports. An
optional module can report aggregated application-start, book-grab, book-import,
and download-failure counts to an OTLP collector you configure. It requires the
module assembly, an explicit enable flag, an HTTPS endpoint, and a bearer token.
It sends no book or author metadata, paths, search terms, log messages,
exception details, or persistent installation identifier. The collector can
still see the source IP address of the HTTPS request. See the
[diagnostics module instructions](src/Bookshelf.Diagnostics/README.md) for the
data schema and installation steps.

## Upstream and license

BookshelfNG is derived from [Bookshelf](https://github.com/pennydreadful/bookshelf),
a revival of [Readarr](https://github.com/Readarr/Readarr), and
[Prowlarr](https://github.com/Prowlarr/Prowlarr). Those projects are licensed
under GPLv3, and BookshelfNG is distributed under the terms of GPLv3. See
[LICENSE](LICENSE.md).

Copyright 2025–2026.
