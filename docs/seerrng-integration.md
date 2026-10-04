# SeerrNG integration and security

BookshelfNG can serve the ebook and audiobook entries in SeerrNG from one
process, one database, and one media library. Configure both SeerrNG entries
with the same BookshelfNG URL and API key. The service entries can still use
different SeerrNG root folders. BookshelfNG stores ebook and audiobook paths
separately, while quality and metadata profiles remain shared on the author
record.

BookshelfNG exposes `/api/v1/system/capabilities` with the
`seerrng-bookshelf` contract. SeerrNG can use the format-scoped API aliases
`/readarr/{gr|hc}/ebook/api/v1` and `/readarr/{gr|hc}/audiobook/api/v1`, where
`gr` and `hc` identify the provider-ID dialect. These are two views of the same
Readarr-compatible API and do not create separate libraries.
Older BookshelfNG versions continue to use the ordinary `/api/v1` routes.

Book statistics retain the Readarr-compatible aggregate fields and also expose
`ebookFileCount`, `audiobookFileCount`, `ebookSizeOnDisk`, and
`audiobookSizeOnDisk`. SeerrNG uses the count for the configured service type,
so an ebook does not make an audiobook request appear available (or vice
versa). The capability response advertises this with
`features.formatSpecificAvailabilityStatistics`; clients that do not recognize
the fields can continue using `bookFileCount` and `sizeOnDisk`.

## Credentials

New BookshelfNG configurations require Forms authentication for the web UI.
The global API key is accepted through `X-Api-Key` (recommended) and the
legacy `?apikey=` query parameter used by Readarr-compatible clients. URLs can
be retained in browser history, application logs, and proxy access logs. To
require header credentials, set `BOOKSHELF_ALLOW_API_KEY_QUERY=false`.
SignalR's `access_token` query parameter remains available for WebSocket
compatibility.

For a separate SeerrNG credential, set `BOOKSHELF_SEERRNG_API_KEY` from a
secret manager and enter that value in both SeerrNG service entries. Generate a
different value from the global API key. This credential is restricted to
status/capability, library, profile, root-folder, queue, book-file, book-history,
and command-status reads; author/book add or update; and a `BookSearch` command
for one book at a time. It cannot access system configuration, API-key
management, other commands, file deletion, or author deletion. Rotate it by
replacing the secret and restarting BookshelfNG; remove the environment setting
to revoke it. All API keys must be treated as credentials and sent over HTTPS
when traffic crosses an untrusted network.

## Proxy, CORS, and network exposure

The Docker quick start publishes port `8787` on loopback. Keep that binding
when a reverse proxy runs on the same host. For a remote proxy, configure
`Readarr__Server__TrustedProxies` as a comma-separated list of exact proxy IPs
or CIDRs, for example `10.0.0.10,10.20.0.0/24`. Forwarded client IP and HTTPS
headers are ignored unless the request came from one of those trusted proxy
addresses. Only one proxy hop is accepted. Do not trust the whole LAN or an
unrestricted container network unless every host there is trusted.

Cross-origin browser access is disabled unless explicit origins are listed in
`Readarr__Server__AllowedOrigins`, separated by commas. Enter full origins,
including scheme and port, such as `https://requests.example.com`. Server-to-
server SeerrNG requests do not need CORS.

Kestrel caps request bodies at 16 MiB by default. Set
`Readarr__Server__MaxRequestBodySize` to change the limit; accepted values are
clamped between 1 MiB and 128 MiB. Backup restore uploads retain their separate
1 GB endpoint limit.

The API rate limiter allows 600 requests per minute per client IP by default.
Set `Readarr__Server__RequestsPerMinute` to tune it from 60 to 6,000 requests
per minute. Requests rejected by the limiter return `429` with a `Retry-After`
header. Correct proxy trust settings are required when clients share an IP
through a reverse proxy.

## Storage and migration

Do not run multiple active BookshelfNG replicas against one SQLite database.
For SeerrNG's two service entries, point both to the same BookshelfNG instance.
Moving from two separate BookshelfNG databases requires an explicit media and
metadata migration; BookshelfNG does not merge databases, histories, profiles,
or settings automatically. Back up both databases and media before moving any
files.
