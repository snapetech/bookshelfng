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

## Azure Pipelines

`Build_Platform_RuntimePacks` builds Linux x86 packs from source, verifies and
collects the FreeBSD packs, and publishes the `platform-runtime-packs`
artifact. The Linux, macOS, and Windows backend jobs add those packages as a
local NuGet source before restoring and publishing all runtime identifiers.
The package job then creates the existing Linux x86 and FreeBSD archives from
those backend outputs.

Linux x86 and FreeBSD test jobs install their platform runtime from that same
artifact. The artifact also carries the architecture-independent .NET 10.0.401
SDK files used by `dotnet test`; the platform runtime host, Core runtime, and
ASP.NET Core shared framework come from the target platform's runtime build.
[`scripts/install-dotnet10-platform-runtime.sh`](../scripts/install-dotnet10-platform-runtime.sh)
assembles that test environment under the path supplied by the pipeline.

## Building locally

Install .NET SDKs 10.0.110, 10.0.111, and 10.0.401 side by side under one
`DOTNET_ROOT`. Docker, Git, Python 3, `curl`, `tar`, and `unzip` are also
required; use Python 3.9 or later. Then run:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export DOTNETVERSION=10.0.401
./scripts/build-dotnet10-platform-runtime-packs.sh
export CUSTOM_RUNTIME_PACKS_DIR="$(pwd)/_temp/platform-runtime-packs/packages"
./build.sh --backend --enable-extra-platforms
```

The pack builder writes the six platform NuGet packages under
`_temp/platform-runtime-packs/packages`. `build.sh` adds Linux x86 and FreeBSD
to the runtime identifier list, enables those identifiers in the .NET 10 SDK,
and uses the local packages during restore. The backend build fails with a
specific missing-package message if the custom pack directory is absent or
incomplete.

The ordinary `dotnet build` works with the standard SDK for the standard
targets. To include Linux x86 and FreeBSD, run the platform pack builder first
and pass `--enable-extra-platforms` to `build.sh`.
