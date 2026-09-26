# Moving existing media

Changing an author's ebook or audiobook path controls where future imports,
upgrades, and renames go. To relocate files already in the library, use
**Move existing ebooks** or **Move existing audiobooks** under that format's
path in the author edit window.

BookshelfNG previews the tracked media files and their registered book
sidecars, estimates copy space, lists missing sources, and blocks destinations
that would overwrite existing files. Confirming the preview queues a background
command. Author-wide metadata and extras stay in the author's default `Path`.
Files unknown to the BookshelfNG database are not swept into the move.

## Bulk moves and legacy two-instance deployments

To move a format for several authors, open the author library, choose **Author
Editor**, select authors (or select all), then choose **Move selected media**.
Pick ebooks or audiobooks and a destination root folder. BookshelfNG preserves
each author's current folder name below that root and previews the complete
batch before it can be queued. A conflict anywhere in the batch blocks the
whole operation.

This is a file move for records in the current instance's database. It does not
merge catalogs or databases from another BookshelfNG instance. For a legacy
ebook/audiobook deployment with two instances, move the tracked format from
each old instance separately, then import or rescan the media in the instance
you are keeping. Database merging is not currently supported.

Move operations require the instance API key. BookshelfNG currently has one
global API key with full administrator-level access; it does not have separate
administrator and regular-user roles.

## API

Both endpoints require the normal `X-Api-Key` header. `id` is the author ID;
`destinationPath` is the absolute path for that format.

```sh
curl --request POST \
  --header "X-Api-Key: $BOOKSHELF_API_KEY" \
  --header 'Content-Type: application/json' \
  --data '{"id":42,"format":"audiobook","destinationPath":"/books/audio/Author Name"}' \
  http://localhost:8787/api/v1/author/media-move/preview
```

Review the preview's `files`, `warnings`, `conflicts`, `totalSize`, and
`canMove`. Start the move by sending the same request to
`/api/v1/author/media-move/start` with its `previewToken` included. The response
contains the queued command resource; read its progress from
`GET /api/v1/command/{id}`. The server rejects a stale preview and requires a
fresh one if tracked files or destination conditions change.

Starting the move also saves that format path for future imports. Missing
sources are shown and skipped. Existing destination files are never replaced.

For a bulk move, send `authorIds`, `format`, and `destinationRootPath` to
`/api/v1/author/media-move/bulk/preview`:

```sh
curl --request POST \
  --header "X-Api-Key: $BOOKSHELF_API_KEY" \
  --header 'Content-Type: application/json' \
  --data '{"authorIds":[42,58],"format":"audiobook","destinationRootPath":"/books/audio"}' \
  http://localhost:8787/api/v1/author/media-move/bulk/preview
```

Review the aggregate counts, each author's destination, `warnings`, `conflicts`,
and `canMove`. Start it at `/api/v1/author/media-move/bulk/start` with the same
request and the returned `previewToken`. The server rechecks the manifest and
queues one background command for the batch.

## CLI

The repository includes `scripts/bookshelf-move-media.sh` for one author and
`scripts/bookshelf-bulk-move-media.sh` for selected authors or every author in
the current instance. Both print the server preview and ask before queuing.
They require `curl` and `jq`:

```sh
export BOOKSHELF_API_KEY='your-api-key'
scripts/bookshelf-move-media.sh \
  --url http://localhost:8787 \
  --api-key "$BOOKSHELF_API_KEY" \
  --author-id 42 \
  --format audiobook \
  --destination '/books/audio/Author Name'
```

Select several authors by ID or use `--all` to include every author in the
current instance:

```sh
scripts/bookshelf-bulk-move-media.sh \
  --url http://localhost:8787 \
  --api-key "$BOOKSHELF_API_KEY" \
  --all \
  --format audiobook \
  --destination-root /books/audio
```

Pass `--yes` only when a reviewed preview and unattended confirmation are
intended. Both helpers still print the preview before queuing. The API key is
the instance's administrator-level credential.
