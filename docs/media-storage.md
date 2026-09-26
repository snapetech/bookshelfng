# Moving existing media

BookshelfNG can change where existing ebook or audiobook files live without
requiring a second library instance. The feature has a per-author workflow and
a bulk workflow. Both operate on files recorded in the current instance's
database; they do not combine application databases.

## Choose the move workflow

### Move one author's format

1. Open **Authors**, edit the author, and find **Ebook Path** or
   **Audiobook Path**.
2. Set the desired format folder if it is not already selected. The format
   path must be inside a configured root folder.
3. Choose **Move existing ebooks** or **Move existing audiobooks** below that
   field.
4. Review the source and destination paths, file list, warnings, conflicts,
   and copy-space estimate.
5. Start the move only when the preview is clear. Follow the queued command in
   **Activity → Queue**.

Changing a format path by itself only changes the destination for future
imports, upgrades, and renames. Starting a move also saves that format path for
future work. For a format path equal to the author's default `Path`, the
override is cleared and the default path is used.

### Move one format for several authors

1. Open the author library and choose **Author Editor**.
2. Select the authors to move, or select all authors.
3. Choose **Move selected media**.
4. Choose **Ebooks** or **Audiobooks**, then choose a configured destination
   root folder. One batch supports up to 1,000 authors.
5. Review the destination path for every author, the aggregate file and space
   counts, and any warnings or conflicts.
6. Choose **Move selected files** to queue the batch. Follow it in
   **Activity → Queue**.

The current author folder name is appended to the destination root. For
example, a root of `/library/audio` and an author folder named
`Ursula K. Le Guin` produce `/library/audio/Ursula K. Le Guin`. The current relative layout
beneath a format folder is retained when it can be determined from the
author's configured paths. Preview every destination before starting.

### Which files are included

BookshelfNG moves registered files of the selected format and book-level
sidecars registered against those files. Book sidecars include metadata files
and configured extras associated with a book file. Their paths are updated in
the current instance's database as they move.

Author-wide metadata and extras stay in the author's default `Path`. Files
that are not registered in the current database are not included. Copy those
separately if they are needed in the new layout. The preview identifies missing
registered source files and skips them; they are not silently counted as moved.

## How preview and execution work

A preview shows the source and destination paths, tracked media and sidecar
counts, missing files, conflicts, and estimated bytes that must be copied.
The estimate may be zero when the move stays on one filesystem. The move
service never overwrites an existing destination file. A file already at the
destination can be reconciled when it matches the previewed file.

For a bulk operation, a destination collision or other conflict anywhere in
the batch blocks queueing the entire batch. Resolve the conflict, then request
a fresh preview. The start request checks that its preview token still matches
the current file and destination state; if anything relevant changed, the
server returns a conflict and requires another preview.

Starting the move queues a background command. Files are processed
sequentially, with the current instance's media-file records updated as each
file completes. This is not a transactional operation: if disk I/O fails,
completed file moves and their database updates remain in place. The command
can stop partway through. Check **Activity → Queue**, fix the storage or
permission problem, and request a fresh preview before retrying. Keep the
source instance and its database available until all moves from that instance
have completed and the destination files are verified.

When the command is queued, BookshelfNG saves the selected format path for the
authors included in the command. That path becomes the target for future
imports, upgrades, and renames in the instance that queued the move.

## Consolidating a legacy two-instance library

This procedure moves files from an old ebook instance and/or audiobook
instance into one BookshelfNG library. It does not migrate either instance's
database. The retained BookshelfNG instance remains the sole catalog after
imports are complete.

### Before moving files

1. Choose which instance and database to keep. Back up both instances'
   configuration and database files, and make sure the media volumes are
   backed up or otherwise recoverable.
2. Decide the final folder layout. Choose whether the formats will share each
   author's default folder or use separate ebook and audiobook folders.
3. Make the destination storage visible and writable from each source
   instance that will perform a move. Container paths are interpreted inside
   the container making the request: the source and destination mount paths
   must point to the intended storage.
4. Add each destination root as a BookshelfNG root folder in every source
   instance that will move files there. The move endpoint validates destination
   paths against that instance's configured roots.
5. Avoid overlapping jobs. Move one source instance at a time and use the
   matching format for that source. If both old catalogs contain records for
   the same physical files, stop and review the catalog before moving to avoid
   duplicate destinations.

### Move each source instance's tracked format

For each old instance, while that instance is running:

- Use **Author Editor → Move selected media** in the web UI, or call the bulk
  API/CLI against that source instance.
- Choose the format stored in that instance (usually **Ebooks** on the old
  ebook instance and **Audiobooks** on the old audiobook instance).
- Select the destination root that matches the final layout, review the whole
  preview, and queue the move.
- Wait for the command to finish. If it fails, keep that source database,
  resolve the error, preview again, and verify the files before moving on.

The bulk action uses the old instance's current author folder name under the
destination root. If the old instances use different author folder names,
review the destination list for collisions and decide how to resolve them
before moving. The move updates paths in the source database only; it does not
change the retained instance's author paths or catalog.

### Import into the instance you are keeping

After the source moves finish:

1. Ensure the retained instance can see the destination files using its own
   configured library mounts and root folders.
2. Set the retained instance's ebook and audiobook paths to the intended final
   folders. The move only saves paths in the instance that performed the move.
3. Import or rescan the moved files from the retained instance. Use its
   existing author/book records where they match; use the existing import
   identification workflow to add books that are not already in its catalog.
4. Review book files, sidecars, author paths, and monitoring in the retained
   instance. Copy any needed untracked or author-wide files manually.
5. Keep a backup of the old databases until the retained library is verified.
   Then stop or remove the redundant instance and update any SeerrNG service
   entries to point to the retained BookshelfNG URL and API key.

The import step is necessary because moving a file from one instance only
updates that source instance's database. BookshelfNG does not transfer authors,
book metadata, quality or metadata profiles, monitoring state, history,
download-client settings, or other configuration between databases. Review
these in the retained instance and recreate or import what you need.

## API

All media-move endpoints require the instance's administrator API key in the
`X-Api-Key` header. BookshelfNG has one global API key and no separate
administrator/member roles; any API client that has the key can use the
administrator API. The web UI uses the app's existing access model and cannot
restrict this action to a subset of in-app users because BookshelfNG does not
provide per-user roles.

### One author

Send the author `id`, `format` (either `ebook` or `audiobook`), and absolute
`destinationPath` to `POST /api/v1/author/media-move/preview`:

~~~sh
curl --request POST \
  --header "X-Api-Key: $BOOKSHELF_API_KEY" \
  --header 'Content-Type: application/json' \
  --data '{"id":42,"format":"audiobook","destinationPath":"/books/audio/Author Name"}' \
  http://localhost:8787/api/v1/author/media-move/preview
~~~

Inspect `files`, `warnings`, `conflicts`, `totalSize`, and `canMove` in the
response. Then send the same request with its `previewToken` to
`POST /api/v1/author/media-move/start`. The server recalculates the preview
before queueing and rejects a stale token or any preview that cannot move.
Starting the request also saves the format path for future imports.

The accepted response contains a command resource. Poll
`GET /api/v1/command/{id}` for status and progress.

### Several authors

Send the selected `authorIds`, `format`, and absolute `destinationRootPath`
to `POST /api/v1/author/media-move/bulk/preview`. The API accepts up to 1,000
distinct positive author IDs in one request.

~~~sh
curl --request POST \
  --header "X-Api-Key: $BOOKSHELF_API_KEY" \
  --header 'Content-Type: application/json' \
  --data '{"authorIds":[42,58],"format":"audiobook","destinationRootPath":"/books/audio"}' \
  http://localhost:8787/api/v1/author/media-move/bulk/preview
~~~

Review aggregate counts, each author's destination, missing files, warnings,
conflicts, copy-space estimate, and `canMove`. To queue it, send the same
request plus the returned `previewToken` to
`POST /api/v1/author/media-move/bulk/start`. The server recalculates the batch
preview; it returns a conflict if the preview is stale or the batch is not
safe to start. One background command handles the queued batch. Poll
`GET /api/v1/command/{id}` to follow it.

## CLI

The repository provides `scripts/bookshelf-move-media.sh` for one author and
`scripts/bookshelf-bulk-move-media.sh` for a selected author list or every
author in the current instance. Both require `curl` and `jq`, print a server
preview, and ask before queueing. Use the API key belonging to the instance
whose files are being moved.

Move one author:

~~~sh
export BOOKSHELF_API_KEY='your-api-key'
scripts/bookshelf-move-media.sh \
  --url http://localhost:8787 \
  --api-key "$BOOKSHELF_API_KEY" \
  --author-id 42 \
  --format audiobook \
  --destination '/books/audio/Author Name'
~~~

Select authors by ID, or use `--all` to include every author in the current
instance. The `--all` option does not reach other BookshelfNG instances; run
the command separately against each source instance during consolidation.

~~~sh
scripts/bookshelf-bulk-move-media.sh \
  --url http://localhost:8787 \
  --api-key "$BOOKSHELF_API_KEY" \
  --all \
  --format audiobook \
  --destination-root /books/audio
~~~

The CLI accepts at most 1,000 authors per batch, matching the API. Use multiple
batches for larger libraries. Pass `--yes` to skip the final interactive
confirmation only after reviewing the printed preview. The script still
previews the request before queueing it.
