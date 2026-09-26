# BookshelfNG parity and differentiation roadmap

**Status:** active planning document, reviewed 2026-09-26. This is a working
roadmap, not a promise that every item is scheduled or will ship.

## Goal

Make BookshelfNG a complete single-instance ebook and audiobook manager with
the capabilities users expect from Chaptarr, while improving compatibility,
metadata transparency, import safety, privacy, and integrations.

Compare the current code with Chaptarr's [README](https://github.com/Chaptarr/chaptarr)
and feature descriptions, rather than using older BookshelfNG documentation.
This checkout already supports formats and workflows missing from previous
public BookshelfNG snapshots.

## Verified current capabilities

| Capability | BookshelfNG status | Evidence / boundary |
| --- | --- | --- |
| Ebook and audiobook in one instance and on one book record | Implemented | Multiple ebook formats and audiobook files coexist; imports and upgrades compare compatible format groups. Per-format storage roots and author profiles are still missing. |
| Standard book automation | Implemented | Author/book monitoring, RSS, indexers, download clients, quality profiles, imports, renaming, search, and upgrades are carried from the Readarr lineage. |
| Narrator metadata | Implemented, now used in identification | Narrators are stored separately, searchable, taggable, and used to rank audio imports when both file and edition metadata provide a narrator. |
| Publisher and edition preferences | Partial | Publisher and format influence matching; metadata profiles support weighted edition terms. There is no dedicated dramatized-audio or publisher-specific release model. |
| Series workflows | Implemented | Series metadata, preferred-series selection, and series-pack search exist. Independent series monitoring is not currently modeled; treat it as a possible extension, not a claimed Chaptarr parity gap. |
| Audiobook conversion | Partial | Completed multi-track downloads can be merged into chaptered M4B. The current flow runs only for two or more tracks, creates one chapter per source track, and does not preserve source chapter maps or convert existing/manual-import files. |
| Metadata providers | Implemented with a different model | Native Hardcover, Readarr-compatible metadata, and supplemental catalogs have qualified identities and per-field preferences. Chaptarr uses its own resolver and consensus pipeline; BookshelfNG should keep improving provider provenance and disagreement handling. |
| Readarr continuity | BookshelfNG advantage | Softcover mode retains Goodreads-compatible identity for existing Readarr-style databases; migration tooling can rebuild metadata against Hardcover. Chaptarr documents that its metadata sources are not Readarr-compatible. |
| Built-in integrations | Strong capability in this checkout | Native MyAnonamouse, Calibre Content Server, BookLore BookDrop, Audiobookshelf sidecars, and SeerrNG's Readarr-compatible API are supported. Chaptarr's README does not document equivalent integrations; that is not proof they are absent. |

## Remaining parity work

### 1. Format-specific library policy and storage

BookshelfNG can store an ebook and audiobook under one book, but `Author` still
has one root path, one quality profile, and one metadata profile. Add optional
ebook and audiobook policies so one instance can use separate media roots,
quality choices, and edition preferences. Keep the existing author-level
settings as defaults for backwards compatibility. Migrate conservatively and
preview path changes before moving existing files.

### 2. Audiobook matching and editions

- Keep narrator-aware scoring reliable and expose concise match reasons in
  Manual Import. The first increment now logs the score contribution.
- Model dramatized, multi-part, and publisher-specific releases without
  collapsing distinct editions into a false duplicate.
- Expand audiobook naming and metadata tokens where the underlying provider
  supplies reliable values.

### 3. Complete chapter-aware audio processing

- Preserve valid source chapters when converting or merging audio.
- Generate useful chapters when sources have none, with track titles and
  duration-based boundaries.
- Support a single MP3 source as well as multi-file releases.
- Offer processing for manual imports and existing library files, with an
  explicit preview, output validation, and source-retention policy.
- Keep conversion opt-in and make FFmpeg availability, CPU cost, codec changes,
  and failure fallback clear to operators.

## Ways to go beyond feature parity

### Explainable, correct imports

Show which identifiers and fields drove an edition match, retain alternate
candidates, and make ambiguous decisions reviewable. Use corrections as
explicit local preferences; never silently train from a mistaken automatic
match.

### Provider provenance and recovery

Make it clear which catalog supplied each value, why conflicting values were
kept or replaced, and whether the saved provider identity can still resolve.
Offer safe re-identification and reconciliation with a report and rollback
path rather than changing IDs in place without review.

### Library and media health

Add scans for duplicate files, broken or missing chapters, invalid identifiers,
unmatched narrator/edition details, and files outside configured format roots.
Every proposed repair should be previewable and reversible.

### Operator trust

Preserve the current no-telemetry default, bounded upstream request behavior,
stable IDs, and conservative import fallbacks. Chaptarr also states that it
does not collect telemetry, so privacy is a shared expectation rather than a
differentiator. Keep backup, migration, and release behavior explicit and
repeatable.

## Delivery order

1. **Current increment:** use narrator tags as optional audiobook identity
   evidence. Missing narrator data remains neutral.
2. **Next architectural milestone:** format-specific roots and profiles,
   preserving old author-level defaults and Readarr API compatibility.
3. **Audio processing milestone:** richer chapter preservation and single-file
   conversion, with validation and rollback-safe imports.
4. **Matching milestone:** publisher/dramatized/multipart handling plus
   explainable Manual Import decisions.
5. **Differentiation:** provider provenance, explicit conflict resolution,
   safe reconciliation, and library health reports.

Each user-visible change should ship independently with a release-note
fragment and should keep the existing database and API usable unless a
versioned migration is explicitly designed.
