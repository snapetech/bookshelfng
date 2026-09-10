# bookshelf

## Snapetech BookshelfNG fork

This public fork publishes SeerrNG-compatible images at:

    ghcr.io/snapetech/bookshelfng:softcover
    ghcr.io/snapetech/bookshelfng:hardcover

The `softcover` image includes a compatibility fix for SeerrNG and other
Readarr-compatible clients: `/api/v1/book/lookup` returns nested author and
edition metadata when Bookshelf can resolve it. Without that fix, softcover
lookups can return `editions: []` even when they include `foreignEditionId`,
which makes downstream book-add calls unreliable.

Use `softcover` when you need Goodreads-compatible metadata or when you are
holding an existing Readarr-compatible database in place. Use `hardcover` for
new deployments and for migrated deployments that have rebuilt their books
against Hardcover metadata.

Do not switch an existing softcover/Readarr config to the `hardcover` image by
changing only `METADATA_URL` or the container tag. Goodreads/softcover
`ForeignAuthorId`, `ForeignBookId`, and `ForeignEditionId` values are not
portable to Hardcover. A direct switch can leave books, authors, and editions
that the Hardcover metadata provider cannot resolve.

SeerrNG includes a migration helper for this transition. The supported path is:

1. back up the existing ebook and audiobook config directories;
2. inventory the source database;
3. rebuild strict matches against a temporary Hardcover Bookshelf target;
4. optionally use a softcover Bookshelf endpoint to recover metadata for stale
   IDs;
5. optionally use SeerrNG's deterministic local DB fallback for books that
   Hardcover still cannot import.

The last fallback creates local Bookshelf records with IDs such as
`local:ebook:1076`. Those records are visible through the Bookshelf API, but
they are not native Hardcover metadata records. That fallback is intentionally
owned by the SeerrNG migration tool rather than the Bookshelf image.

Image tags are published by GitHub Actions:

- `softcover`
- `softcover-v0.4.20`
- `softcover-v0.4.20.<run-number>`
- `hardcover`
- `hardcover-v0.4.20`
- `hardcover-v0.4.20.<run-number>`

The stable `softcover` and `hardcover` tags are updated only by releases from
the `main` branch. Pushes to `develop` publish `softcover-develop` and
`hardcover-develop` instead. Downstream applications that use the stable tags
therefore always consume the latest released `main` build.

If anonymous `docker pull ghcr.io/snapetech/bookshelfng:softcover` returns
`denied`, the GHCR package visibility still needs to be changed to public in
GitHub package settings, or Docker needs to be authenticated with package read
access.

### Native Hardcover metadata

The `hardcover` image uses BookshelfNG's native Hardcover GraphQL provider by
default. It handles search, author, work, edition, series, ISBN, and ASIN
lookups directly from BookshelfNG, so a normal Hardcover deployment does not
need rreading-glasses or a metadata proxy in the request path.

Native mode requires a Hardcover API token at runtime:

```env
HARDCOVER=true
HARDCOVER_AUTH=Bearer your-hardcover-api-token
```

`HARDCOVER_API_KEY` is accepted as an alternative variable. The default API
endpoint is `https://api.hardcover.app`; override it with
`HARDCOVER_API_URL` only when using a compatible endpoint or a test service.
The token is sent to Hardcover by BookshelfNG and is not baked into the image.

`METADATA_URL` remains the compatibility fallback. Set
`HARDCOVER_NATIVE=false` to disable direct GraphQL access and route metadata
through that URL instead. This is the setting to use when an existing
rreading-glasses cache must be preserved, when another Readarr-compatible
metadata service is required, or when testing the legacy path. Native mode
does not silently fail over to another service after a GraphQL error; select
the compatibility path deliberately so the active dependency is visible.

Native and compatibility modes use the same Bookshelf metadata models, but
their foreign IDs are not interchangeable with Goodreads/softcover IDs.
Native mode caches responses in the Bookshelf process for the lifetime of the
instance. rreading-glasses remains useful when a durable shared PostgreSQL
cache, a Goodreads-compatible endpoint, or a proxy boundary is more valuable
than removing the extra service.

## Upstream project

This is a revival of [Readarr](https://github.com/Readarr/Readarr). The images
published are configured to use working Goodreads or Hardcover metadata out of
the box.

Bookshelf is an ebook and audiobook collection manager for Usenet and BitTorrent
users. It can monitor multiple RSS feeds for new books from your favorite
authors and will grab, sort, and rename them. Note that only one type of a
given book is supported. If you want both an audiobook and ebook of a given
book you will need multiple instances.

## Getting Started

The container listens on port 8787 and expects a volume mounted at `/config`.

    docker run -p 8787:8787 -v ~/.config/bookshelf:/config ghcr.io/pennydreadful/bookshelf:hardcover

The `softcover` tags use [Goodreads](https://www.goodreads.com) as the metadata
provider. The quality of this metadata is generally poor and contains a lot of
slop. However, it is backward-compatible with existing Readarr databases and
functionality like Goodreads list imports should continue to work normally.

The `hardcover` tags use [Hardcover](https://hardcover.app/home) as a metadata
provider. When `HARDCOVER=true`, BookshelfNG selects the native provider by
default; set `HARDCOVER_NATIVE=false` to use the compatibility endpoint at
`METADATA_URL` instead. Native mode requires `HARDCOVER_AUTH` (or
`HARDCOVER_API_KEY`) at runtime. This metadata is higher quality but isn't
backward-compatible with Goodreads/softcover IDs. Hardcover list imports use
the API key configured in the Bookshelf import-list settings.

## Support

This project won't use Discord for support. If you have a problem please file
an issue or start a discussion.

## Building from source

Install the versions listed in `mise.toml`, then build the backend and frontend
for a Linux x64 host:

    ./build.sh --backend --frontend -r linux-x64 -f net6.0

Additional MSBuild arguments can be passed with `--msbuild-arg`. For example,
to keep a NuGet audit warning visible without treating it as an error:

    ./build.sh --backend --frontend -r linux-x64 -f net6.0 --msbuild-arg "-p:WarningsNotAsErrors=NU1903"

Unknown build options fail fast so misspelled or unsupported flags are not
silently ignored.

## Contributors & Developers

Help is very welcome. Priority is on fixing quality of life issues

- [ ] Monitor series.
- [ ] Support ebook and audio files in the same root.

Already done

- [x] Native support for MyAnonaMouse without Prowlarr.
- [x] Hardcover list import.
- [x] Improved matching.
- [x] Native Hardcover metadata with an explicit compatibility fallback.
- [x] Removed servarr analytics spyware.
- [x] Supports selfhosted metadata (UI or `METADATA_URL` env var).

## Sponsors

If you ever donated to [this](https://opencollective.com/readarr) project you
should request a refund. Those people don't deserve your money.

### License

The is a derivative work of the [Readarr](https://github.com/Readarr/Readarr)
and [Prowlarr](https://github.com/Prowlarr/Prowlarr) projects which are both
licensed [GPLv3](http://www.gnu.org/licenses/gpl.html). This project is
therefore also licensed under the terms of GPLv3.

Copyright 2025-2026
