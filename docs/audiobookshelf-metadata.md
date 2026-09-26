# Audiobook metadata: narrator tags and Audiobookshelf sidecars

BookshelfNG can write OPF metadata files beside book files for Audiobookshelf
to read during its normal library scan. The provider is optional and disabled
until enabled under **Settings > Metadata**.

For each audiobook, the OPF file includes the title, author, available ISBN or ASIN,
description, publisher, language, release year, genres, and selected series
name and position. A metadata profile's preferred series is used when it
matches; otherwise BookshelfNG uses the provider's primary series and then
series position. For multi-part books, BookshelfNG writes one sidecar for the
first part. The OPF file follows the book file when BookshelfNG moves or
renames it.

## Narrator metadata

When a metadata provider supplies an edition narrator, BookshelfNG stores it
separately from the book author. Narrator values appear in book search and can
be used to filter the library. Provider coverage varies; Hardcover maps
narrator, reader, reading, and author/narrator contribution roles into the
narrator field.

During audiobook import identification, BookshelfNG compares narrator credits
read from the audio file with the candidate edition's narrator when both are
available. A matching narrator helps rank similar audiobook editions; missing
narrator data on either side does not count against a candidate.

Audio tag reading recognizes narrator fields in common audiobook tags,
including ID3, Vorbis comments, APE, ASF/WMA, MP4, and Audible metadata. Tag
writing is controlled by **Settings > Metadata > Write Audio Tags**. When
enabled, BookshelfNG writes the selected edition's narrator using the audio
format's tag fields.

For Vorbis comments, BookshelfNG reads all `PERFORMER` values as narrator
credits and falls back to the older `NARRATOR` field when no performer value
exists. When writing Vorbis tags, it writes narrator credits to `PERFORMER`
and clears the legacy `NARRATOR` field, leaving the author credit separate.

Point Audiobookshelf at the same media folders and run its normal library scan
or let its file watcher detect the sidecars. Audiobookshelf reads OPF metadata
from the library files; it does not need an API token or a direct connection to
BookshelfNG. If audio tags contain different values, check Audiobookshelf's
metadata precedence in the library settings.

See the [Audiobookshelf book library documentation](https://audiobookshelf.org/docs/documentation/libraries/book-library/directory-structure/)
for its supported directory layout and OPF sidecar fields.
