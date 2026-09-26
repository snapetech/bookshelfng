# Moving existing media

Changing an author's ebook or audiobook path controls where future imports,
upgrades, and renames go. To relocate files already in the library, use the
separate **Move existing ebooks** or **Move existing audiobooks** action under
that format's path in the author edit window.

BookshelfNG previews the tracked media files and their registered book
sidecars, estimates copy space, lists missing sources, and blocks destinations
that would overwrite existing files. Confirming the preview queues a background
command. Author-wide metadata and extras stay in the author's default `Path`.
Files unknown to the BookshelfNG database are not swept into the move.

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

## CLI

The repository includes `scripts/bookshelf-move-media.sh`, a small API client
that prints the full preview and asks before queuing the operation. It requires
`curl` and `jq`:

```sh
export BOOKSHELF_API_KEY='your-api-key'
scripts/bookshelf-move-media.sh \
  --url http://localhost:8787 \
  --api-key "$BOOKSHELF_API_KEY" \
  --author-id 42 \
  --format audiobook \
  --destination '/books/audio/Author Name'
```

Pass `--yes` only when a reviewed preview and unattended confirmation are
intended. The helper still prints the preview before it queues the move.
