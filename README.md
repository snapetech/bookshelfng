# BookshelfNG

BookshelfNG is a self-hosted ebook and audiobook library manager descended from
Readarr. It monitors RSS feeds for books by authors you follow, searches Usenet
and BitTorrent download clients, and organizes, renames, and upgrades files in
your library. It can run on its own; SeerrNG integration is optional.

Bookshelf supports one format per book in an instance. Run separate instances
if you want to manage both the ebook and audiobook editions of the same title.

## Quick start

The web interface listens on port `8787`. Persist `/config`, and mount your
downloads and library paths where the application and download clients can
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

Start it with `docker compose up -d`, then open `http://localhost:8787` to
configure your root folder, metadata provider, indexers, and download clients.
The image is also available as `ghcr.io/snapetech/bookshelfng:softcover`.

## Metadata providers

BookshelfNG offers two metadata modes:

- **Hardcover** (`hardcover` image): uses BookshelfNG's native Hardcover
  GraphQL provider by default. Set `HARDCOVER=true` and provide
  `HARDCOVER_AUTH=Bearer your-hardcover-api-token` at runtime. You can use
  `HARDCOVER_API_KEY` instead of `HARDCOVER_AUTH`. The default API endpoint is
  `https://api.hardcover.app`; `HARDCOVER_API_URL` can override it. Native
  metadata requests go directly from BookshelfNG to Hardcover; no metadata
  proxy is required. Set `HARDCOVER_NATIVE=false` to use the compatibility
  endpoint configured with `METADATA_URL` instead.
- **Goodreads-compatible** (`softcover` image): retains compatibility with
  existing Readarr databases and Goodreads list imports. Goodreads metadata
  quality is variable.

Goodreads/softcover and Hardcover foreign author, book, and edition IDs are not
interchangeable. Changing an existing database from softcover to Hardcover by
changing only the image tag or metadata URL can leave records that the new
provider cannot resolve. Back up your configuration before changing providers
and follow the migration guidance below when migrating an existing library.

## Using BookshelfNG with SeerrNG

SeerrNG can use BookshelfNG as a Readarr-compatible ebook or audiobook service.
This integration is optional: BookshelfNG runs as a complete library manager
without SeerrNG.

The `softcover` image is useful when retaining a Readarr-compatible database.
Its `/api/v1/book/lookup` response includes nested author and edition metadata
when BookshelfNG can resolve it, which supports downstream book-add requests.

For an existing softcover library migrating to Hardcover, back up the ebook
and audiobook configuration directories first. SeerrNG's migration helper can
inventory the source database, rebuild strict matches against a temporary
Hardcover BookshelfNG target, and optionally use a softcover endpoint or its
deterministic local database fallback for records Hardcover cannot import.
Fallback records such as `local:ebook:1076` are local Bookshelf records, not
native Hardcover metadata records.

## Releases

GitHub Actions publishes these rolling and versioned image tags:

- `softcover`, `softcover-v0.4.20`, `softcover-v0.4.20.<run-number>`
- `hardcover`, `hardcover-v0.4.20`, `hardcover-v0.4.20.<run-number>`

If an anonymous pull returns `denied`, the GHCR package may not be public yet;
you can also authenticate with package read access.

## Support and contributing

Please file a GitHub issue or start a discussion for help. Contributions are
welcome, especially fixes and quality-of-life improvements. Current areas of
interest include monitoring series and supporting ebook and audiobook files
in the same root folder.

## Upstream and license

BookshelfNG is derived from [Bookshelf](https://github.com/pennydreadful/bookshelf),
a revival of [Readarr](https://github.com/Readarr/Readarr), and
[Prowlarr](https://github.com/Prowlarr/Prowlarr). Those projects are licensed
under GPLv3, and BookshelfNG is distributed under the terms of GPLv3. See
[LICENSE](LICENSE.md).

Copyright 2025–2026.
