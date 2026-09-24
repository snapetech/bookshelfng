# Search and download workflows

BookshelfNG supports book discovery, interactive release selection, existing
torrent adoption, and queue cleanup. Automatic and user-started searches use
the selected runtime catalog sources and configured indexers.

## Search the catalog from Add

The **Add** page waits for you to submit a search by default. Enter a title or
author and select **Search** (or press Enter). Turn on **Search as you type**
to search as the query changes. This preference is saved in the current
browser, so it does not change search behavior in other browsers.

The catalogs available to Add are controlled by the runtime source selection
in **Settings > Metadata**. See the main README's
[metadata source settings](../README.md#metadata-sources) for provider setup
and source identity behavior.

## Search indexers and adopt an existing torrent

Use interactive release search on a book or author to choose a result from a
configured indexer. If BookshelfNG finds the matching torrent in the selected
download client, it offers to adopt that item. Adoption adds the existing
download to BookshelfNG's tracked queue without submitting it to the indexer
or asking the client to download it again. BookshelfNG then processes the
tracked item through its normal import workflow when it completes.

If the torrent has already disappeared from the client, search again before
grabbing it. BookshelfNG checks that the existing item is still available
before adopting it.

## Search for a series pack

On an author's series list, use the series search button to find releases
that contain multiple books. After a pack completes, use Manual Import to
assign each file to its book and edition. See the dedicated
[series pack search guide](series-pack-search.md) for the review flow.

## Ignore a queue item

For an unmatched download that should remain in the download client, select
**Ignore** from the queue removal action. BookshelfNG stops processing and
removes its queue entry; the download and its files remain in the client.
This is useful for unrelated files or downloads you want to manage manually.

Implementation references: [Add page search behavior](../frontend/src/Search/AddNewItem.js),
[existing torrent adoption](../src/NzbDrone.Core/Download/DownloadService.cs),
[queue ignore handling](../src/NzbDrone.Core/Download/IgnoredDownloadService.cs).
