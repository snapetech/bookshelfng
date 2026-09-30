# Distribution channels

Stable releases are tagged `main-vMAJOR.MINOR.PATCH.BUILD`. The release
workflow runs only after the tag is verified as reachable from `main` and the
normal BookshelfNG build succeeds. It prepares the GitHub Release as a draft,
builds platform archives and packages, attaches checksums, then publishes the
release and sends the existing Discord announcement.

## Published artifacts

| Channel | Distribution | Notes |
| --- | --- | --- |
| GitHub Releases | Self-contained Windows, macOS, Linux, and FreeBSD archives; Windows x64 setup installer | Every asset has a SHA-256 file. Linux x86 and FreeBSD use the pinned .NET 10 runtime pack process. |
| Debian / Ubuntu | `.deb` package | Installs a systemd service and `/var/lib/bookshelfng` data directory. |
| Fedora / RPM | `.rpm` and COPR repository | Service package; COPR repository is `slskdn/bookshelfng`, built for Fedora 44 and Rawhide. |
| Arch Linux | `bookshelfng-bin` on AUR | Binary package with systemd service files. |
| Ubuntu PPA | Signed source package | Requires the Launchpad PPA and signing key configured in repository Actions secrets. |
| Flatpak | `.flatpak` bundle on GitHub Releases | Uses Freedesktop Platform 26.08. This bundle is not a Flathub publication. |
| AppImage | Linux x64 `.AppImage` | Portable launcher; data stays in the user's XDG data directory. |
| Chocolatey | `bookshelfng` Windows package | Downloads the checksummed x64 release asset and registers a Windows service. |
| Helm | OCI chart in GHCR | Chart is versioned with each stable BookshelfNG release. |
| Containers | GHCR and Docker Hub | Multi-architecture `amd64` and `arm64`, with Softcover and Hardcover tags. |
| Unraid | Community application template | Uses the moving `ghcr.io/snapetech/bookshelfng:hardcover` tag by default; use `:softcover` to keep Readarr-compatible database lineage. |

The distribution workflow reports publisher channels that lack credentials as
skipped. It does not claim an upload succeeded unless the publisher command
returns success. Configure the publisher settings and credentials listed below
before expecting an external package store to receive a release.

## Publisher credentials and settings

| Secret or variable | Used by |
| --- | --- |
| `AUR_SSH_KEY` | Pushes the `bookshelfng-bin` package to AUR. Add the matching public key to the AUR account. |
| `COPR_WEBHOOK_URL` | Package-scoped COPR custom webhook URL for the `bookshelfng` package. |
| `GPG_PRIVATE_KEY` | Signs source packages before upload to Launchpad. |
| `LAUNCHPAD_PPA` | Existing PPA target (`ppa:<owner>/<archive>`), set as an Actions secret or repository variable. |
| `CHOCOLATEY_API_KEY` | Pushes `bookshelfng` packages to Chocolatey Community Repository. |
| `DISCORD_RELEASE_WEBHOOK` | Required. Sends the curated release notes and image digests to the BookshelfNG release channel. |

Before making a GitHub Release public, the workflow validates configured
optional publishers. It skips malformed AUR, COPR, Launchpad, or Chocolatey
settings and records the reason in the workflow summary. Missing optional
credentials are also reported and skipped. The required Discord webhook is
validated before the release draft is created, so malformed settings do not
leave a public release without its announcement.

The Launchpad upload runs only when `GPG_PRIVATE_KEY` contains an importable
private signing key and `LAUNCHPAD_PPA` names an existing public PPA. Set
`LAUNCHPAD_PPA` as a repository variable or Actions secret; the publisher
account must have permission to upload to it.

The COPR build uses a custom webhook URL rather than a COPR API token. Use the
package-scoped URL from the `bookshelfng` package settings, in this form:
`https://copr.fedorainfracloud.org/webhooks/custom/<ID>/<UUID>/bookshelfng/`.
The workflow skips the COPR channel if the URL does not match this route. Treat
the URL as a secret because anyone who obtains it can request a build. COPR's
[custom webhook documentation](https://docs.copr.fedorainfracloud.org/user_documentation.html#custom-webhook)
describes this endpoint and the package-specific URL segment. The release
workflow calls it only after the GitHub Release is public. COPR then downloads
the versioned source RPM and its SHA-256 file from that release, verifies the
checksum, and extracts the spec and sources for its build. The source
preparation script is [`packaging/rpm/copr-source.sh`](../packaging/rpm/copr-source.sh).

Build output from `main` is not sent to third-party package stores; store
updates are tied to verified stable release tags.

## First publication setup

Before the first external upload, register `bookshelfng` on the Chocolatey
Community Repository, add the AUR SSH public key to the publisher account, and
create a Launchpad PPA for the publisher account. For COPR, create the public
`slskdn/bookshelfng` project with Fedora 44 and Rawhide x86_64 chroots, then add
a custom source package named `bookshelfng`. Configure its custom source script
to bootstrap the script from the release tag:

```bash
#!/usr/bin/env bash
set -euo pipefail
tag="$(tr -d '\r\n' < hook_payload)"
[[ "$tag" =~ ^main-v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]
curl --fail --location --silent --show-error \
  "https://raw.githubusercontent.com/snapetech/bookshelfng/main/packaging/rpm/copr-source.sh" \
  | bash -s -- "$tag"
```

Configure the script build dependencies as `bash curl cpio rpm`. In the COPR
project settings, create a custom webhook for the `bookshelfng` package and save
its URL as the `COPR_WEBHOOK_URL` Actions secret in `snapetech/bookshelfng`.
COPR's custom webhook sends the request body to the source script in
`hook_payload`; the release workflow sends the verified release tag after
publishing its assets. This setup does not need `COPR_LOGIN` or `COPR_TOKEN`.
Rotate the webhook URL in COPR and update the GitHub secret if it is exposed.

Add the remaining account credentials above to the repository's Actions
secrets. Package identifiers are reserved by their respective services, so
confirm ownership before the first publish. Flatpak is provided as a GitHub
Release bundle; Flathub hosting is a separate, maintainer-reviewed publication
path.
