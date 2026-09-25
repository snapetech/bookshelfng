# Self-hosting catalog deployment

This directory is the source of truth for the Docker-based self-hosting
catalog submissions for BookshelfNG. The adapters use the public Docker Hub
image so new installations do not need a registry login.

## Current stable image

```text
docker.io/snapetech/bookshelfng:hardcover-v0.4.21.37
```

The image supports `linux/amd64` and `linux/arm64` and serves its web UI on
TCP port `8787`. Persist `/config`, mount the managed book and audiobook
library at `/books`, and mount the download destination at `/downloads` when a
download client shares that path.

The `softcover-v0.4.21.37` tag is available for existing Goodreads-compatible
libraries. The platform-specific submissions prepared from this contract are
CasaOS / ZimaOS, Umbrel, TrueNAS, Cosmos, CapRover, Portainer, Co-op Cloud,
Cloudron, and StartOS. Unraid and YunoHost are maintained separately.
