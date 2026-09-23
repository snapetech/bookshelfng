# BookshelfNG

BookshelfNG is a self-hosted ebook and audiobook library manager descended
from Readarr. Follow authors and books, monitor indexers for new releases,
automatically grab downloads, and organize, rename, and upgrade files in your
library. BookshelfNG is a complete app on its own. It does not require SeerrNG
or a metadata proxy.

BookshelfNG is for people who want Readarr-style book automation with a choice
of metadata sources: Goodreads-compatible metadata for existing libraries,
Hardcover metadata fetched directly from Hardcover, or a compatible hosted or
self-hosted metadata service.

Google Books, Open Library, Library of Congress, and Goodreads-compatible
Apify Actors are not currently BookshelfNG runtime metadata providers. SeerrNG
uses some of these catalogs only in its migration recovery helper; see the
[SeerrNG metadata source support matrix](https://github.com/Snapetech/seerrng/blob/main/docs/using-seerr/bookshelf-metadata-sources.md)
for exact scope and configuration. A runtime provider must support search and
the follow-up book, author, and edition lookups using its own IDs before it can
be safely exposed in BookshelfNG.

## What makes BookshelfNG different

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
- **Match real world releases more reliably.** Import identification considers
  edition metadata and identifiers, uses improved title and author matching,
  and can match title-only ebooks and audiobooks when author information is
  absent. Search result ordering is preserved so results stay predictable.
- **Import books from the lists you use.** BookshelfNG supports Goodreads
  shelves, owned books, series, and Listopia lists, plus native Hardcover list
  imports. Hardcover imports honor the lists selected in configuration.
- **Search MyAnonamouse directly.** The native MyAnonamouse indexer lets you
  use the tracker without adding Prowlarr as an intermediary.
- **Connect current download clients.** BookshelfNG supports qBittorrent 5.2
  authentication alongside the download clients inherited from Readarr.
- **Keep the familiar automation.** Monitor authors and books, search RSS
  feeds, apply quality and metadata profiles, manage download clients, scan
  and import existing files, and rename and upgrade releases automatically.
- **Keep library activity private.** BookshelfNG removes Servarr's Sentry
  analytics and exception-reporting integration. An independent diagnostics
  module is available for operators who explicitly install and enable it; the
  standard app image contains no reporting module or telemetry SDK.

BookshelfNG retains the Readarr-compatible API and the broader Readarr
library-management workflow. It supports one format per book in an instance;
run separate ebook and audiobook instances if you want both formats of the
same title.

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
`softcover` image is available for Goodreads-compatible libraries.

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
compatible endpoint or test service. Credentials are provided at runtime and
are not baked into the image.

Native mode handles transient request failures and sparse or nullable metadata
responses, maps Hardcover work and edition records into Bookshelf's library
model, and caches responses in the BookshelfNG process for the lifetime of the
instance. It does not silently switch to another metadata service after an API
error.

Text search fetches Hardcover's result IDs in one batched book query instead
of making a separate detail request for every result. ISBN/ASIN searches and
edition lookups fetch the parent work in the same GraphQL operation. API
requests are paced at one per second. BookshelfNG honors Hardcover's
`Retry-After` and exhausted rate-limit headers; when a limit is reached, it
stops sending further requests from this process until the reported reset and
returns an error for calls made during that cooldown instead of retrying into
the limit.

### Optional additional runtime catalogs

The Hardcover image keeps Hardcover as its primary metadata source and merges
public catalog results into normal book searches. Library of Congress is
enabled by default. Google Books is enabled automatically when
`GOOGLE_BOOKS_API_KEY` is set. The Goodreads-compatible Apify adapter remains
opt-in because Actor usage may be metered. Supported values for
`BOOKSHELF_METADATA_SOURCES` are `googlebooks`, `loc`, and `apify-goodreads`.
When set, this variable replaces the defaults; an empty value disables all
additional catalogs.

```env
HARDCOVER=true
HARDCOVER_AUTH=Bearer your-hardcover-api-token
GOOGLE_BOOKS_API_KEY=your-google-books-api-key
```

With this configuration Google Books and Library of Congress are both
queried alongside Hardcover. Without a Google API key, Library of Congress
still runs and Google Books is skipped. To keep only LOC, set
`BOOKSHELF_METADATA_SOURCES=loc`; to disable the additions, set
`BOOKSHELF_METADATA_SOURCES=`.

To add a Goodreads-compatible Apify Actor:

```env
BOOKSHELF_METADATA_SOURCES=googlebooks,loc,apify-goodreads
HARDCOVER_APIFY_GOODREADS_ACTOR=publisher~goodreads-scraper
HARDCOVER_APIFY_TOKEN=your-apify-token
# Override only when the Actor does not use searchQueries and maxItems:
HARDCOVER_APIFY_GOODREADS_INPUT_TEMPLATE={"searchQueries":[{{query}}],"maxItems":10}
```

Google Books public searches require a Google API key; user OAuth is not
needed. Library of Congress search is public and rate limited. Requests are
paced to one per 3.2 seconds per Bookshelf process and successful responses
are cached for one day. If multiple Bookshelf processes share an outbound IP,
set a source list that avoids querying LOC from every process. The optional
Apify adapter runs a user-selected Actor, whose schema, availability, terms,
and pricing are controlled by its publisher. The default Actor input is
`{"searchQueries":[{{query}}],"maxItems":10}`; a custom JSON template must
include the literal `{{query}}` placeholder, which Bookshelf replaces with a
JSON-escaped search string. Actor output is normalized from common Goodreads
scraper fields, and results missing a title or author are skipped.

Each provider result receives a namespaced foreign ID such as
`googlebooks:volume-id`, `loc:<encoded-record-url>`, or
`apify-goodreads:<encoded-record-key>`. Bookshelf uses those same IDs for
subsequent book and author metadata lookups; it does not coerce them into
numeric Goodreads IDs. The selected provider names and credentials must remain
available when the library is refreshed. Search results are cached for 10
minutes, Google Books and LOC responses for one day, and Apify result sets for
one day. Provider failures are logged independently; other configured sources
continue to return results.

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
interchangeable. The optional additional catalogs documented above provide
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
notes and announce the successfully built softcover and hardcover images to
Discord. Pull requests require a release-note fragment for user-facing changes;
internal-only work must be marked `release-note: none`. See
[`release-notes/README.md`](./release-notes/README.md) for the format and
preview command.

If an anonymous pull returns `denied`, the GHCR package may not be public yet;
you can also authenticate with package read access.

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
