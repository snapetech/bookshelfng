# Distribution channels

Stable releases are tagged `main-vMAJOR.MINOR.PATCH.BUILD`. The release
workflow runs only after the tag is verified as reachable from `main` and the
normal BookshelfNG build succeeds. It prepares the GitHub Release as a draft,
builds platform archives and packages, attaches checksums, then publishes the
release and sends the existing Discord announcement.

## Published artifacts

| Channel | Distribution | Notes |
| --- | --- | --- |
| GitHub Releases | Self-contained Windows, macOS, Linux, and FreeBSD archives | Every asset has a SHA-256 file. Linux x86 and FreeBSD use the pinned .NET 10 runtime pack process. |
| Debian / Ubuntu | `.deb` package | Installs a systemd service and `/var/lib/bookshelfng` data directory. |
| Fedora / RPM | `.rpm` and COPR SRPM | Service package; COPR repository is `slskdn/bookshelfng`. |
| Arch Linux | `bookshelfng-bin` on AUR | Binary package with systemd service files. |
| Ubuntu PPA | Signed source package | Requires the Launchpad PPA and signing key configured in repository Actions secrets. |
| Snap Store | Strictly confined `bookshelfng` Snap | Requires the Snap name to be registered and store credentials configured. |
| Flatpak | `.flatpak` bundle on GitHub Releases | Uses Freedesktop Platform 26.08. This bundle is not a Flathub publication. |
| AppImage | Linux x64 `.AppImage` | Portable launcher; data stays in the user's XDG data directory. |
| Chocolatey | `bookshelfng` Windows package | Downloads the checksummed x64 release asset and registers a Windows service. |
| Helm | OCI chart in GHCR | Chart is versioned with each stable BookshelfNG release. |
| Containers | GHCR and Docker Hub | Multi-architecture `amd64` and `arm64`, with Softcover and Hardcover tags. |
| Unraid | Community application template | Uses the Docker Hub Hardcover tag by default; edit the image tag for Softcover. |

The distribution workflow reports publisher channels that lack credentials as
skipped. It does not claim an upload succeeded unless the publisher command
returns success. Configure the missing repository secrets listed below before
expecting an external package store to receive a release.

## Required GitHub Actions secrets

| Secret | Used by |
| --- | --- |
| `AUR_SSH_KEY` | Pushes the `bookshelfng-bin` package to AUR. Add the matching public key to the AUR account. |
| `COPR_LOGIN`, `COPR_TOKEN` | Submits SRPM builds to Fedora COPR. |
| `COPR_USERNAME` | Optional COPR account name; defaults to the SeerrNG publisher account `slskdn`. |
| `GPG_PRIVATE_KEY` | Signs source packages before upload to Launchpad. |
| `SNAPCRAFT_STORE_CREDENTIALS` | Uploads the Snap to the stable channel. Use Snapcraft's exported, scope-limited credentials. |
| `CHOCOLATEY_API_KEY` | Pushes `bookshelfng` packages to Chocolatey Community Repository. |
| `DISCORD_RELEASE_WEBHOOK` | Existing BookshelfNG release announcement channel. |

The `LAUNCHPAD_PPA` repository variable defaults to `ppa:keefshape/bookshelfng`;
it can also be supplied as an Actions secret to override that target. The
package source files are in [`packaging/`](../packaging/). The COPR job
creates or updates the `bookshelfng` project under the authenticated COPR
account and builds it for Fedora 44 and Rawhide. Set the `COPR_PROJECT`
repository variable only if the project should use another name. Build output
from `main` is not sent to third-party package stores; store updates are tied to
verified stable release tags.

## First publication setup

Before the first external upload, register `bookshelfng` on the Snap Store and
Chocolatey Community Repository, add the AUR SSH public key to the publisher
account, and create a Launchpad PPA owned by the Snapetech publisher account.
The COPR job creates its project on first release. Add the account credentials
above to the `snapetech/bookshelfng` repository's Actions secrets. Package identifiers are
reserved by their respective services, so confirm ownership before the first
publish. Flatpak is provided as a GitHub Release bundle; Flathub hosting is a
separate, maintainer-reviewed publication path.
