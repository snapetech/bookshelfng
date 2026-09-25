#!/usr/bin/env bash
set -euo pipefail

repo="snapetech/bookshelfng"
release_base="https://github.com/${repo}/releases/download"
tag="${1:-}"

if [[ -z "$tag" && -r hook_payload ]]; then
  tag="$(tr -d '\r\n' < hook_payload)"
fi

if [[ ! "$tag" =~ ^main-v([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)$ ]]; then
  echo 'COPR webhook payload must be a verified main-vMAJOR.MINOR.PATCH.BUILD release tag.' >&2
  exit 2
fi

version="${BASH_REMATCH[1]}"
source_rpm="bookshelfng-${version}-1.src.rpm"
resultdir="${COPR_RESULTDIR:-.}"
workdir="$(mktemp -d)"
trap 'rm -rf -- "$workdir"' EXIT

curl --fail --location --silent --show-error --retry 3 --retry-all-errors \
  --output "${workdir}/${source_rpm}" \
  "${release_base}/${tag}/${source_rpm}"
curl --fail --location --silent --show-error --retry 3 --retry-all-errors \
  --output "${workdir}/${source_rpm}.sha256" \
  "${release_base}/${tag}/${source_rpm}.sha256"

(cd "$workdir" && sha256sum --check "${source_rpm}.sha256")

mkdir -p "$resultdir"
resultdir="$(cd "$resultdir" && pwd -P)"
(
  cd "$resultdir"
  rpm2cpio "${workdir}/${source_rpm}" | cpio -idm --no-absolute-filenames
)

for required_file in \
  bookshelfng.spec \
  "bookshelfng-${version}.tar.gz" \
  bookshelfng.service \
  bookshelfng.env \
  bookshelfng.sysusers \
  bookshelfng.tmpfiles; do
  if [[ ! -f "${resultdir}/${required_file}" ]]; then
    echo "The COPR source RPM is missing ${required_file}." >&2
    exit 1
  fi
done

echo "Prepared the verified BookshelfNG ${version} source RPM contents for COPR."
