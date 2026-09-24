# BookLore integration

BookshelfNG can send newly imported book files to BookLore's BookDrop queue.
BookDrop provides a review step in BookLore before files are added to its
library.

## Configure the destination

In **Settings > Connect**, add **BookLore** as a notification destination and
enter:

- **Base URL**: the BookLore address reachable from the BookshelfNG process.
  The default is http://localhost:6060.
- **Username** and **Password**: a BookLore account with permission to upload
  files.

Use the connection test to check the address and credentials. When BookshelfNG
reports a release import, it uploads each imported file to BookLore BookDrop.
Files are streamed from disk during upload.

## Use the canonical BookLore URL

BookshelfNG does not follow redirects for the BookLore login or upload
requests. Set the destination to BookLore's final canonical URL, including any
reverse-proxy path, so the login and upload endpoints respond directly. A
redirected login or upload is reported as a failure.

For authenticated integrations generally, BookshelfNG does not forward
credentials, cookies, or request bodies across redirect origins. See the
[redirect security release note](../release-notes/security-cross-origin-redirects.md).

## Related guides

- [Capabilities and compatibility](capabilities-and-compatibility.md)
- [Search and download workflows](search-and-downloads.md)
