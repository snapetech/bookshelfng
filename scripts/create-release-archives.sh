#!/usr/bin/env bash
set -euo pipefail

tag="${1:?Usage: create-release-archives.sh <main-v...> [output-dir]}"
output_dir="${2:-dist-release}"
runner_temp="${RUNNER_TEMP:-${TMPDIR:-/tmp}}"

[[ "$tag" =~ ^main-v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || {
  echo "Invalid BookshelfNG release tag: $tag" >&2
  exit 2
}

mkdir -p "$output_dir"
if [[ "$output_dir" != /* ]]; then
  output_dir="$(cd "$output_dir" && pwd)"
fi
source_date_epoch="$(git show -s --format=%ct "${tag}^{commit}")"

release_rids=(
  linux-x64
  linux-x86
  linux-arm64
  linux-musl-x64
  linux-musl-arm64
  freebsd-x64
  osx-x64
  osx-arm64
  win-x64
  win-x86
)

make_archive() {
  local rid="$1"
  local source_dir="_artifacts/$rid/net10.0/Readarr"
  local archive_base="BookshelfNG-${tag}-${rid}"
  local archive
  local staging
  local host_executable

  if [[ "$rid" == win-* ]]; then
    archive="$output_dir/$archive_base.zip"
  else
    archive="$output_dir/$archive_base.tar.gz"
  fi

  [[ -d "$source_dir" ]] || {
    echo "Missing package output for $rid: $source_dir" >&2
    exit 1
  }
  if [[ -f "$source_dir/Readarr" ]]; then
    host_executable="Readarr"
  elif [[ -f "$source_dir/Readarr.exe" ]]; then
    host_executable="Readarr.exe"
  elif [[ -f "$source_dir/Readarr.Console.exe" ]]; then
    host_executable="Readarr.Console.exe"
  else
    echo "Package output for $rid does not contain the BookshelfNG host executable." >&2
    exit 1
  fi

  staging="$(mktemp -d "$runner_temp/bookshelfng-${rid}.XXXXXX")"
  trap 'rm -rf "$staging"' RETURN
  mkdir -p "$staging/$archive_base"
  cp -a "$source_dir/." "$staging/$archive_base/"
  cp LICENSE.md "$staging/$archive_base/LICENSE.md"
  cp docs/standalone-install.md "$staging/$archive_base/INSTALL.md"
  if [[ "$host_executable" == "Readarr" ]]; then
    chmod 0755 "$staging/$archive_base/Readarr"
  fi
  if [[ "$rid" == win-* ]]; then
    cat > "$staging/$archive_base/BookshelfNG.cmd" <<EOF
@echo off
"%~dp0${host_executable}" %*
EOF
  fi
  find "$staging/$archive_base" -exec touch -h -d "@$source_date_epoch" {} +

  if [[ "$rid" == win-* ]]; then
    (cd "$staging" && zip -X -q -r "$archive" "$archive_base")
  else
    tar --sort=name --owner=0 --group=0 --numeric-owner --mtime="@$source_date_epoch" \
      -C "$staging" -czf "$archive" "$archive_base"
  fi
  (cd "$output_dir" && sha256sum "$(basename "$archive")" > "$(basename "$archive").sha256")
  rm -rf "$staging"
  trap - RETURN
}

for rid in "${release_rids[@]}"; do
  make_archive "$rid"
done

for rid in osx-x64 osx-arm64; do
  app_dir="_artifacts/${rid}-app/net10.0/Readarr.app"
  archive_base="BookshelfNG-${tag}-macos-${rid#osx-}.app"
  archive="$output_dir/$archive_base.zip"
  [[ -d "$app_dir" ]] || {
    echo "Missing macOS application bundle for $rid: $app_dir" >&2
    exit 1
  }
  staging="$(mktemp -d "$runner_temp/bookshelfng-${rid}-app.XXXXXX")"
  mkdir -p "$staging/$archive_base"
  cp -a "$app_dir/." "$staging/$archive_base/"
  find "$staging/$archive_base" -exec touch -h -d "@$source_date_epoch" {} +
  (cd "$staging" && zip -X -q -r "$archive" "$archive_base")
  (cd "$output_dir" && sha256sum "$(basename "$archive")" > "$(basename "$archive").sha256")
  rm -rf "$staging"
done

find "$output_dir" -maxdepth 1 -type f -printf '%f\n' | sort
