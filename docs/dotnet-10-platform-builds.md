# .NET 10 builds for Linux x86 and FreeBSD

BookshelfNG targets .NET 10 on the same platforms it packages: Windows x64 and
x86, macOS x64, Linux x64 and x86, Linux musl x64 and ARM64, and FreeBSD x64.
The standard .NET SDK supplies the packs for the mainstream targets. Linux x86
and FreeBSD use additional runtime packs because matching .NET 10 packs are not
available from the standard NuGet feeds used by this repository.

## Pack sources

Linux x86 runtime, apphost, and ASP.NET Core packs are cross-compiled from the
Microsoft `dotnet/runtime` and `dotnet/aspnetcore` `v10.0.12` source tags. The
runtime build uses Microsoft's Azure Linux 3.0 .NET 10 x86 cross-build image,
pinned to digest
`sha256:ecc9c107cd33f8303cba2280eb1537f045acaa88fc0f00780ea6232d19e942b8`.
The builder uses the SDK versions pinned by those source trees (10.0.110 and
10.0.111) and produces the standard `Microsoft.NETCore.App` and
`Microsoft.AspNetCore.App` NuGet packs at version 10.0.12.

BookshelfNG publishes without ReadyToRun or Native AOT. The Linux x86 source
build therefore disables ReadyToRun, omits the crossgen package, and skips the
ASP.NET composite ReadyToRun pack. This keeps the app's runtime output focused
on the runtime, apphost, and framework packs BookshelfNG consumes.

FreeBSD x64 packs and its runtime host are taken from the
`Thefrank/dotnet-freebsd-crossbuild` `v10.0.112-amd64-freebsd-14` release. The
downloaded archive is SHA-256 checked against
`7e032c2024cfb17ac3bb27ab8d35234a6e74f5ede16e66ca8420bcf66028d978` before
any package or runtime files are extracted. This is a community source build,
not a Microsoft-published FreeBSD SDK.

The pinned inputs are declared in
[`scripts/build-dotnet10-platform-runtime-packs.sh`](../scripts/build-dotnet10-platform-runtime-packs.sh).
When updating .NET servicing versions, update both source tags, the FreeBSD
release URL and checksum, expected NuGet package names, and the runtime SDK
assembly performed by the pack builder together.

## GitHub Actions release builds

The [`Release Distributions`](../.github/workflows/release-distribution.yml)
workflow builds these packs from the verified release tag and uploads the
`bookshelfng-platform-runtime-packs` artifact. A retry may reuse an artifact
from a successful compatible runtime-pack job; the workflow checks its source
commit and build inputs before accepting it. The release-assets job adds the
Linux x86 and FreeBSD packages as a local NuGet source before publishing the
platform archives.

The same artifact carries the architecture-independent .NET 10.0.401 SDK files
used by `dotnet test`; the target platform's runtime host, Core runtime, and
ASP.NET Core shared framework come from the platform packs. The workflow uses
[`scripts/install-dotnet10-platform-runtime.sh`](../scripts/install-dotnet10-platform-runtime.sh)
to assemble that test environment.

## Use a published Docker image

To run BookshelfNG in Docker, use the published image described in the
[README quick start](../README.md#quick-start). Docker Compose pulls the full
image, including the backend and web UI; no source checkout, .NET SDK, Node.js,
or `build.sh` is needed.

The path `/app/readarr/bin/UI` is inside the running container. It is not a
folder on the host. Inspect it with:

```bash
docker exec <container-name> ls /app/readarr/bin/UI
```

Replace `<container-name>` with the name shown by `docker ps`.

## Build a Docker image from source

Use this path when testing changes from a source checkout. The Dockerfile
consumes the backend and UI produced in `_output`, then copies them into the
image. Build both parts before building the image:

```bash
./build.sh --backend --frontend
docker buildx build --load --platform linux/amd64 \
  --file docker/Dockerfile \
  --tag bookshelfng:local \
  --build-arg GIT_BRANCH=local \
  --build-arg COMMIT_HASH=local \
  --build-arg BUILD_DATE=local \
  --build-arg IMAGE_VERSION=local \
  --build-arg IMAGE_FLAVOR=hardcover \
  --build-arg METADATA_URL=https://hardcover.bookinfo.pro \
  --build-arg HARDCOVER=true \
  .
```

Change `linux/amd64` to `linux/arm64` when building for an ARM64 host. The
Dockerfile copies `_output/UI` into `/app/readarr/bin/UI` and selects the
matching Linux musl backend output. Leave off `--runtime` and `--framework` on
this path; the standalone `linux-x64` build command below creates a different
runtime output. `--packages` is for standalone app directories and is not
needed for this Docker build.

For a one-off UI check against an existing container, you can copy the compiled
assets with `docker cp _output/UI/. <container-name>:/app/readarr/bin/UI/`.
Replace `<container-name>` with the name shown by `docker ps`. That changes only
the current container; rebuilding or replacing it discards the copy. Build a
custom image when you need a repeatable result.

## Build and package a standalone app locally

These commands create an app directory to run directly on the host; they do
not build a Docker image. A runnable BookshelfNG build needs both the .NET
backend and the browser UI. Install Node.js 20 and Yarn Classic 1.22.19 as well
as the .NET SDK. The `--frontend` option installs the locked Yarn dependencies
and compiles the UI; `--packages` copies the UI beside the backend executable.
A backend-only build does not produce a runnable app with a web interface.

For a standard Linux x64 build, use the SDK runtime packs:

```bash
./build.sh --backend --frontend --packages --framework net10.0 --runtime linux-x64
```

The packaged app is under
`_artifacts/linux-x64/net10.0/Readarr/`; it contains the `Readarr` executable
and its `UI/` directory.

To also build the Linux x86 and FreeBSD packages, install .NET SDKs 10.0.110,
10.0.111, and 10.0.401 side by side under one `DOTNET_ROOT`. Docker, Git,
Python 3, `curl`, `tar`, and `unzip` are also required; use Python 3.9 or later.
Then run:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export DOTNETVERSION=10.0.401
./scripts/build-dotnet10-platform-runtime-packs.sh
export CUSTOM_RUNTIME_PACKS_DIR="$(pwd)/_temp/platform-runtime-packs/packages"
./build.sh --backend --frontend --packages --enable-extra-platforms
```

The pack builder writes the six platform NuGet packages under
`_temp/platform-runtime-packs/packages`. `build.sh` adds Linux x86 and FreeBSD
to the runtime identifier list, enables those identifiers in the .NET 10 SDK,
and uses the local packages during restore. The packaged apps, including the
UI, are written under `_artifacts/<runtime>/net10.0/Readarr/`. The backend
build fails with a specific missing-package message if the custom pack
directory is absent or incomplete.

For backend tests only, `./build.sh --backend --enable-extra-platforms` builds
the .NET projects and test assemblies, but does not compile or package the UI.
