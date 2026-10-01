# Catalog metadata composition and unmapped-file recovery

BookshelfNG can use records from different enabled catalogs to fill different
fields on the same library book. The book keeps its original provider identity;
supplemental records contribute metadata, covers, or author images when their
records can be matched safely.

## Combine metadata fields and images

Enable the catalogs you want in **Settings > Metadata > Runtime Catalog
Sources**. In **Per-field source preferences**, choose an enabled catalog for
each field you want to source separately. For example, a Hardcover record can
remain the book identity while Open Library supplies genres and Google Books
supplies a cover. Choose **Use the selected book's source** to keep a field
from the primary record.

BookshelfNG uses a matching ISBN for edition-specific values such as publisher,
language, release date, or page count. For work-level fields such as title,
description, genres, and covers, it first looks for an ISBN match and then can
use an exact normalized title-and-author match across editions. Author images
use the configured **Author image** source and require a matching author name
and book work. When available, BookshelfNG fetches the matched catalog's author
profile to fill the image. Empty or unavailable fields leave the current value
in place.

Alternate catalogs never replace or impersonate one another's IDs. A book can
be enriched from several catalogs while retaining a real Hardcover ID,
another provider-qualified ID, or an existing primary-provider ID. Hardcover
compatibility is provided by BookshelfNG's normal book and edition model and
API response shapes; an actual Hardcover ID is not required.

The same field preferences are available through the metadata provider config
API. For example, `metadataCoverSourcePreference` and
`metadataAuthorImageSourcePreference` can select separate image catalogs on
`PUT /api/v1/config/metadataprovider/1`.

Author portraits use their own source preference, independent of book fields.
For example, Open Library can supply author portraits while Internet Archive
supplies a book's description and cover and another catalog supplies its
genres.

## Retry unmapped library files

The **Unmapped Files** page lists files still missing a book edition. Use
**Retry unmapped files** to search their containing folders again with the
currently enabled catalogs. A successful match adds the author, book, and
edition through the normal library import flow. The action refreshes the list
when the command finishes and reports how many files were mapped and how many
still need a match. Repeated retry commands are serialized.

This retry deliberately revisits unchanged files whose previous catalog
search found no match. Regular scans continue to skip those files until their
metadata or file state changes.

### REST API

Queue the same operation with the command API:

```http
POST /api/v1/command
X-Api-Key: <api-key>
Content-Type: application/json

{"name":"RetryUnmappedFiles"}
```

The accepted response includes the command ID. Poll `GET
/api/v1/command/{id}` for its status and final message. Read the current
unmapped list with `GET /api/v1/bookfile?unmapped=true`.

### Command line

Set the instance URL and API key, then queue the retry:

```bash
export BOOKSHELF_URL="http://localhost:8787"
export BOOKSHELF_API_KEY="your-api-key"
scripts/bookshelf-retry-unmapped.sh
```

Use `--wait` to wait for completion, `--json` for a machine-readable command
response, or both. `jq` is required for `--wait`; `curl` is required for all
calls.

```bash
scripts/bookshelf-retry-unmapped.sh --wait --json
```

The API and CLI commands use the same catalog selection and rate-limit
behavior as normal import matching. They do not rewrite existing files or
require authors and books to be created manually.
