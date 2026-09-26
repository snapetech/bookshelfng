# Import matching and multiple formats

BookshelfNG matches downloaded or scanned files to catalog editions, then
tracks the imported files under a book. One BookshelfNG instance and database
can track multiple ebook formats and audiobook files on the same book record.
Separate instances are not required to support both media types. Import and
upgrade decisions keep incompatible formats separate.

When SeerrNG routes ebooks and audiobooks through separate service entries,
both entries can use the same BookshelfNG URL and API key. Each author can keep
the default path or set optional ebook and audiobook folder overrides in the
author editor. Overrides affect future imports, upgrades, and renames; setting
one does not move existing files. Quality and metadata profiles are still shared by
both formats. Separate format profiles are tracked in the
[parity roadmap](maintainers/bookshelfng-parity-roadmap.md).

## Formats on one book

Different ebook formats can coexist on the same book. An EPUB import does not
replace a MOBI, AZW3, or PDF file; an ebook does not replace an audiobook.
Quality checks and upgrades compare files within a compatible format group.
This lets one library keep converted ebook formats alongside its audiobook
edition.

Supported extensions are listed in the
[capabilities and compatibility guide](capabilities-and-compatibility.md#supported-file-formats).
Audiobook M4B creation is an optional separate workflow; see
[audiobook M4B merging](audiobook-m4b-merging.md).

## Automatic import title threshold

**Settings > Media Management > Minimum Auto-Import Title Match (%)** is an
advanced setting that controls how strong the title match must be before an
automatic import is accepted. It defaults to 70 percent and accepts values
from 50 to 99. Higher values are stricter. Raise the threshold when similar
titles by the same author are being matched to the wrong book; lower it when
your indexers use titles that differ from the catalog.

BookshelfNG keeps the best fuzzy title match among the candidates it checks.
Identifiers such as ISBN and ASIN, author and title information, and edition
details also help rank editions. Sparse or conflicting file tags can still
require manual review.

## Weighted edition preferences

Each metadata profile has a **Preferred Edition Terms** field. Add one
positive weight and one term per line, separated by |. For example:

~~~text
3|audible
2|kindle
1|ebook
~~~

BookshelfNG looks for each term, without case sensitivity, in the edition
title, format, and publisher. A higher matching weight is preferred. If
several terms match, the highest matching weight applies. An edition that
matches none of the listed terms receives no preference benefit.

Assign different metadata profiles to authors or root folders when ebook and
audiobook imports should prefer different edition types. The author's profile
is used when available; for a new author, import matching uses the metadata
profile selected for the root folder.

## Preferred series

The **Preferred Series** field on a metadata profile accepts series names in
priority order, separated by commas or lines. Matching is case-insensitive
and accepts a name found within a series title. BookshelfNG selects a matching
preferred series before the provider's primary-series flag, then uses the
lowest numbered position. Unnumbered series sort after numbered entries.

The selected series is used in generated filenames and in metadata written
for Calibre and Audiobookshelf. Leave the field empty to use provider primary
status and series position without a custom preference.

## Related guides

- [Author metadata refresh](author-metadata-refresh.md)
- [Audiobook narrator tags and Audiobookshelf sidecars](audiobookshelf-metadata.md)
- [Search and download workflows](search-and-downloads.md)

Implementation references: [format compatibility](../src/NzbDrone.Core/MediaFiles/MediaFileExtensions.cs),
[edition scoring](../src/NzbDrone.Core/MediaFiles/BookImport/Identification/DistanceCalculator.cs),
[import selection](../src/NzbDrone.Core/MediaFiles/BookImport/Identification/IdentificationService.cs),
[preferred series selection](../src/NzbDrone.Core/Books/Model/SeriesBookLinkExtensions.cs).
