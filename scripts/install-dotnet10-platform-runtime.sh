#!/usr/bin/env bash
set -euo pipefail

rid="${1:?Usage: install-dotnet10-platform-runtime.sh <linux-x86|freebsd-x64> <artifact-root> [dotnet-root]}"
artifact_root="${2:?Usage: install-dotnet10-platform-runtime.sh <linux-x86|freebsd-x64> <artifact-root> [dotnet-root]}"
dotnet_root="${3:-${DOTNET_ROOT:-/opt/dotnet}}"
sdk_version="10.0.401"

case "$rid" in
    linux-x86|freebsd-x64) ;;
    *) echo "Unsupported platform runtime: $rid" >&2; exit 2 ;;
esac

mkdir -p "$dotnet_root"
tar -xzf "$artifact_root/runtimes/$rid-runtime.tar.gz" -C "$dotnet_root"
mkdir -p "$dotnet_root/shared/Microsoft.AspNetCore.App/10.0.12"
tar -xzf "$artifact_root/runtimes/$rid-aspnetcore-shared.tar.gz" -C "$dotnet_root/shared/Microsoft.AspNetCore.App/10.0.12"

mkdir -p "$dotnet_root/sdk" "$dotnet_root/packs"
cp -a "$artifact_root/sdk/sdk/$sdk_version" "$dotnet_root/sdk/"
cp -a "$artifact_root/sdk/packs/." "$dotnet_root/packs/"
if [ -d "$artifact_root/sdk/sdk-manifests" ]; then
    mkdir -p "$dotnet_root/sdk-manifests"
    cp -a "$artifact_root/sdk/sdk-manifests/." "$dotnet_root/sdk-manifests/"
fi

chmod a+x "$dotnet_root/dotnet"
export DOTNET_ROOT="$dotnet_root"
export DOTNET_MULTILEVEL_LOOKUP=0
export PATH="$dotnet_root:$PATH"
echo "##vso[task.setvariable variable=DOTNET_ROOT]$dotnet_root"
echo "##vso[task.prependpath]$dotnet_root"
dotnet --info
