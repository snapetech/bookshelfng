# Multi-file audiobook merging

BookshelfNG can combine a completed, identified audiobook download into one
chaptered M4B file. The feature is opt-in and disabled by default.

Set `BOOKSHELF_M4B_MERGE=true` in the BookshelfNG container environment. The
standard Docker image includes FFmpeg. For native installs, make `ffmpeg`
available on `PATH`, or set `BOOKSHELF_FFMPEG_PATH` to the executable path.

BookshelfNG merges two or more approved audio files that match the same book
and edition during completed-download processing. Manual imports and library
scans do not run this step, and existing M4B files are left alone. It orders
tracks by disc and track tags when present, then by numbered file name, and
uses each track title or filename as the chapter title. The output is stereo
AAC at 44.1 kHz and 128 kbps by default; set
`BOOKSHELF_M4B_AAC_BITRATE_KBPS` to a value from 48 to 320 to change the
bitrate.

If FFmpeg is unavailable, track durations cannot be read, conversion fails, or
the merged output is rejected by the import decision maker, BookshelfNG
removes the temporary output and imports the original files. Original tracks
are removed only after the M4B imports successfully in move mode. Copy mode
and downloads whose client does not allow moving files keep the source tracks.
The merge re-encodes audio, so it uses CPU and output quality depends on the
configured AAC bitrate.

The merge runs during completed-download processing. Existing library scans
and manual imports are unchanged. Existing standalone M4B downloads are also
left alone.

## Implementation references

- [M4B merge, conversion, validation, and cleanup](../src/NzbDrone.Core/MediaFiles/AudiobookM4bMergeService.cs)
- [Completed-download import integration](../src/NzbDrone.Core/MediaFiles/DownloadedBooksImportService.cs)
- [FFmpeg package in the container image](../docker/Dockerfile)
