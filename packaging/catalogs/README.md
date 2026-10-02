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
Cloudron, and StartOS. Unraid is maintained separately.

The YunoHost package source lives in `packaging/yunohost/`. Local commits sync
to the sibling `bookshelfng_ynh` checkout's `testing` branch when the optional
`scripts/install-yunohost-sync-hook.sh` hook is installed. After changes merge
to this repository's `main`, the Package sync workflow pushes the package code
to `YunoHost-Apps/bookshelfng_ynh:testing`. It requires a `YUNOHOST_REPO_TOKEN`
Actions secret with `contents:write` access to that repository. Package code
updates are submitted from `testing` to `main` as a pull request.

The package pins a versioned, checksummed archive from the latest stable
`main-v*` release. Its `latest_github_release` autoupdate strategy lets the
YunoHost updater detect later stable builds and propose manifest updates; it
does not install untagged builds from every commit on `main`.
