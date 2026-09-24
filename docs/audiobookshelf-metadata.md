# Audiobookshelf metadata sidecars

BookshelfNG can write OPF metadata files beside book files for Audiobookshelf
to read during its normal library scan. The provider is optional and disabled
until enabled under **Settings > Metadata**.

For each audiobook, the OPF file includes the title, author, available ISBN or ASIN,
description, publisher, language, release year, genres, and primary series
name and position. For multi-part books, BookshelfNG writes one sidecar for
the first part. The OPF file follows the book file when BookshelfNG moves or
renames it.

Point Audiobookshelf at the same media folders and run its normal library scan
or let its file watcher detect the sidecars. Audiobookshelf reads OPF metadata
from the library files; it does not need an API token or a direct connection to
BookshelfNG. If audio tags contain different values, check Audiobookshelf's
metadata precedence in the library settings.

See the [Audiobookshelf book library documentation](https://audiobookshelf.org/docs/documentation/libraries/book-library/directory-structure/)
for its supported directory layout and OPF sidecar fields.
