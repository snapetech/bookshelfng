#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(git -C "$script_dir/.." rev-parse --show-toplevel)"
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

unraid_url="${BOOKSHELFNG_UNRAID_GIT_URL:-https://github.com/snapetech/bookshelfng-unraid.git}"
unraid_branch="${BOOKSHELFNG_UNRAID_BRANCH:-main}"

if ! command -v rsync >/dev/null 2>&1; then
	echo "rsync is required to verify package repository sync." >&2
	exit 1
fi

for source_path in packaging/unraid/README.md; do
	if [[ ! -f "$repo_root/$source_path" ]]; then
		echo "Committed package source is missing $source_path." >&2
		exit 1
	fi
done

git clone --quiet --depth 1 --branch "$unraid_branch" "$unraid_url" "$work_dir/unraid"

check_tree() {
	local label="$1"
	local source_dir="$2"
	local target_dir="$3"
	shift 3
	local -a excludes=("$@")
	local changes

	changes="$(rsync --recursive --links --perms --checksum --omit-dir-times \
		--delete --dry-run --itemize-changes --out-format='%i %n%L' \
		"${excludes[@]}" "$source_dir/" "$target_dir/")"
	if [[ -n "$changes" ]]; then
		echo "$label package checkout differs from the committed package source:" >&2
		printf '%s\n' "$changes" >&2
		return 1
	fi
	echo "$label package checkout matches the committed package source."
}

check_file() {
	local label="$1"
	local source_file="$2"
	local target_file="$3"
	if [[ ! -f "$target_file" ]] || ! cmp -s "$source_file" "$target_file"; then
		echo "$label package file differs from the committed package source: ${source_file##*/}" >&2
		return 1
	fi
}

# The Unraid repository also owns release notes outside the package source.
# Match exactly the paths managed by sync-unraid-package.sh.
for file in README.md ca_profile.xml; do
	check_file "Unraid" "$repo_root/packaging/unraid/$file" "$work_dir/unraid/$file"
done
check_tree "Unraid templates" "$repo_root/packaging/unraid/templates" "$work_dir/unraid/templates"
if [[ -d "$repo_root/packaging/unraid/docs" ]]; then
	check_tree "Unraid docs" "$repo_root/packaging/unraid/docs" "$work_dir/unraid/docs"
elif [[ -e "$work_dir/unraid/docs" ]]; then
	echo "Unraid docs package checkout contains files absent from the committed package source." >&2
	exit 1
fi
