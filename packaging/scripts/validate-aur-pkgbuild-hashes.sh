#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 || ! -f "$1" ]]; then
  echo "Usage: $0 <PKGBUILD>" >&2
  exit 2
fi

pkgbuild="$1"
mapfile -t sources < <(bash -c 'source "$1"; printf "%s\n" "${source[@]}"' bash "$pkgbuild")
mapfile -t checksums < <(bash -c 'source "$1"; printf "%s\n" "${sha256sums[@]}"' bash "$pkgbuild")

[[ ${#sources[@]} -eq ${#checksums[@]} ]] || {
  echo 'PKGBUILD source and checksum counts differ.' >&2
  exit 1
}

for index in "${!sources[@]}"; do
  checksum="${checksums[$index]}"
  [[ "$checksum" =~ ^[0-9a-f]{64}$ ]] || {
    echo "Invalid SHA-256 checksum for ${sources[$index]}" >&2
    exit 1
  }
  if [[ "${sources[$index]}" != *://* ]]; then
    source_name="${sources[$index]##*/}"
    source_name="${source_name%%::*}"
    source_path="$(dirname "$pkgbuild")/$source_name"
    [[ -f "$source_path" ]] || {
      echo "Missing local source: $source_path" >&2
      exit 1
    }
    actual="$(sha256sum "$source_path" | awk '{print $1}')"
    [[ "$actual" == "$checksum" ]] || {
      echo "Checksum mismatch for $source_path" >&2
      exit 1
    }
  fi
done
