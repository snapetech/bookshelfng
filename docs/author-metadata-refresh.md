# Author metadata storage an| Author-ID not-found log | The provider did not return a usable author record. BookshelfNG keeps the existing author and leaves its metadata freshness unchanged; check the following “Keeping author” warning. |
BookshelfNG keeps the current author catalog data in its application database.
The database is the durable cache: the app does not need a separate permanent
in-memory cache to remember an author it already knows. Provider responses may
also be cached in process for shorter periods, but those caches are performance
optimizations and are cleared when the process exits.

## What is stored

Each author record points to an `AuthorMetadata` record through
`AuthorMetadataId`. That metadata contains the provider's foreign author ID,
names and sort names, aliases, biography, status, images, links, genres,
ratings, and other catalog fields. The author row stores `LastInfoSync` with the
library's normal author settings. Books, editions, and series are stored as
their own related database records.

Stored metadata has no time-to-live that deletes it when it becomes old. A
successful refresh updates the saved record; a provider error before new author
data can be applied leaves the existing record available. Keep the application's
`/config` volume persistent in container deployments so the database survives
container replacement.

Provider IDs are part of the identity of that stored data. A Goodreads
compatible author ID is not interchangeable with a Hardcover or additional
catalog ID. Changing metadata providers does not convert the library; follow
the [Hardcover migration guidance](../README.md#moving-an-existing-library-to-hardcover)
when changing a library's metadata identity.

## Avoiding duplicate author lookups

When BookshelfNG refreshes a book, it still needs the book or work details from
the configured metadata provider. If that response identifies an author that
already exists locally under the same provider foreign ID, BookshelfNG reuses
the local author ID and metadata record. The work response's embedded author
metadata and the local `AuthorMetadataId` are used to attach the book to that
author without loading the author's full catalog and book list a second time.
The normal author refresh is responsible for updating the author's full
catalog record. If the parent author is missing locally, BookshelfNG fetches
author details to create or restore that parent.

Adding a book also does not automatically mean that BookshelfNG will fetch the
author catalog. The book-added handler queues an author refresh only when the
author has never been synced or the freshness policy below says an update is
due. This keeps duplicate book events from creating duplicate author requests.

## Automatic author freshness policy

`LastInfoSync` is compared with UTC time. The checks run in this order:

| Stored author state | Automatic refresh decision |
| --- | --- |
| `LastInfoSync` is missing | Refresh; the author has not been validated yet. |
| Last sync was more than 30 days ago | Refresh. |
| Last sync was within the past 12 hours | Skip. This guard runs before activity checks. |
| Author status is `Continuing` and last sync was more than 2 days ago | Refresh. |
| Latest known book release date is within the past 30 days | Refresh. |
| None of the conditions above match | Skip until a later sweep or an explicit refresh. |

The 30-day stale check runs first. The 12-hour guard suppresses repeat work
after a recent successful sync, including when an author has an active status
or a recently released book. For a continuing author, the two-day rule applies
after that short-term guard. For other authors, a recent release can trigger a
refresh, while an older inactive author is normally checked again after its
`LastInfoSync` becomes more than 30 days old.

Providers can optionally supply a list of changed author IDs during a scheduled
refresh. When that list is available, it narrows the scheduled work to those
IDs. Native Hardcover does not expose the legacy Readarr changed-author feed,
so BookshelfNG uses the local freshness policy for scheduled Hardcover author
refreshes.

## Refreshes that bypass the age check

- **Manual refresh:** a manually triggered author refresh bypasses the
  freshness check for the selected author. A manually triggered full refresh
  applies to all authors in that command.
- **Metadata profile change:** changing an author's metadata profile queues a
  forced refresh so the author catalog can be re-filtered under the new
  profile.
- **Initial author sync:** an author without `LastInfoSync` is due immediately.

These paths intentionally revalidate data even when the author was recently
synced. Ordinary book-added and scheduled automatic work remains subject to
the freshness check.

## Hardcover response cache and request pacing

The native Hardcover provider has a separate process-local response cache:

| Response | Cache lifetime |
| --- | ---: |
| Author detail | 30 minutes |
| Work detail | 6 hours |
| Search results | 10 minutes |

These entries are keyed by provider IDs or normalized search terms. They reduce
duplicate requests while BookshelfNG is running, but are not durable and do not
replace the database copy of an author. Restarting BookshelfNG clears them.

The Hardcover client spaces API requests at one request per second in each
BookshelfNG process. On a response with rate-limit information, it pauses all
Hardcover requests in that process until the later applicable reset. It reads
`Retry-After`, exhausted `RateLimit` buckets, and legacy
`X-RateLimit-Remaining` / `X-RateLimit-Reset` and daily reset headers. A 429
without a usable reset header causes a one-minute pause. Calls made during the
pause fail without sending another request. Network failures and transient
server errors use a bounded retry policy; a 429 is not retried into the limit.

The client also reads the daily quota from `RateLimit-Policy` and remaining
capacity from `RateLimit`, with legacy daily limit and remaining headers as a
fallback. Background catalog lookups stop before using the final 10% of the
reported daily quota, rounded up to at least one request. Explicit catalog
searches from the user interface can use that reserve, while still respecting
an actual rate-limit pause or exhausted bucket. Background lookup resumes when
the reported daily window resets. The reserve is calculated from the API
response, so it follows the quota assigned to the Hardcover account.

Pacing and cooldown state are process-local. Separate BookshelfNG instances do
not share response caches or coordinate their request rate, even if they use
the same Hardcover token or outbound IP. Run one metadata-active instance per
token/IP when you need the strictest shared pacing.

The Goodreads-compatible provider has its own short-lived in-process author
response cache. Other metadata providers also define their own cache and
request behavior; do not infer their TTLs from Hardcover's values above.

## What happens when a refresh fails

A network error, HTTP error, invalid response, or Hardcover rate-limit response
is logged by the refresh command and does not apply new author data. Since no
usable author response was applied, the stored author remains available and its
`LastInfoSync` is not made fresh by that failed lookup. A later eligible refresh
can try again.

A provider's explicit “author not found” result or a lookup without a usable
author record is treated as missing remote data. BookshelfNG keeps the existing
local author record even when it has no media files linked to it. The failed
lookup does not update its metadata or make the record appear fresh, so a later
eligible or manual refresh can try again. This protects valid aliases and pen
names that a provider does not recognize.

## Troubleshooting refresh logs

Use the application's log viewer and search around the time of the attempted
refresh:

| Log message | Meaning |
| --- | --- |
| `Skipping refresh of author: <name>` | The command deliberately skipped this author. Check nearby trace messages for the freshness rule; a provider's changed-author list can also narrow a scheduled run. |
| `Updating Info for <name>` | The refresh began and is loading remote author data. |
| `Couldn't refresh info for <author>` | The command caught a provider or refresh exception. Include the preceding provider error and HTTP status when reporting it. |
| `Hardcover ... pausing all requests until ... UTC` | Hardcover supplied a rate-limit reset and the process entered cooldown. |
| `Hardcover rate limit pause is active ... no request was sent` | A call reached the client during that cooldown and was rejected locally without another HTTP request. |
| `Could not find author with id <id>` | The provider lookup returned the explicit not-found path; inspect the following refresh log for whether the local author was retained or removed. |

The refresh policy writes trace-level messages for the missing/30-day rule,
12-hour guard, continuing-author rule, and recent-release rule. If the ordinary
log level does not include trace output, the `Skipping refresh` info message
still confirms that the author was gated by the command.

## Implementation references

- [Author database model](../src/NzbDrone.Core/Books/Model/Author.cs)
- [Stored author metadata fields](../src/NzbDrone.Core/Books/Model/AuthorMetadata.cs)
- [Author freshness policy](../src/NzbDrone.Core/Books/Utilities/ShouldRefreshAuthor.cs)
- [Author refresh scheduling and force behavior](../src/NzbDrone.Core/Books/Services/RefreshAuthorService.cs)
- [Shared refresh persistence and not-found behavior](../src/NzbDrone.Core/Books/Services/RefreshEntityServiceBase.cs)
- [Book refresh and local-parent reuse](../src/NzbDrone.Core/Books/Services/RefreshBookService.cs)
- [Book-added refresh gate](../src/NzbDrone.Core/Books/Handlers/BookAddedHandler.cs)
- [Forced refresh after metadata-profile changes](../src/NzbDrone.Core/Books/Handlers/AuthorEditedHandler.cs)
- [Refresh command fields](../src/NzbDrone.Core/Books/Commands/RefreshAuthorCommand.cs)
- [Hardcover cache, batching, request pacing, and rate-limit cooldown](../src/NzbDrone.Core/MetadataSource/Hardcover/HardcoverMetadataProxy.cs)
- [Compatible provider lookup and short-lived author cache](../src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs)
- [Manual command trigger](../src/Readarr.Api.V1/Commands/CommandController.cs)

## External API references

- [Hardcover API getting started and authentication](https://github.com/hardcoverapp/hardcover-docs/blob/main/src/content/docs/api/Getting-Started.mdx)
- [Hardcover GraphQL author schema](https://github.com/hardcoverapp/hardcover-docs/blob/main/src/content/docs/api/GraphQL/Schemas/Authors.mdx)
