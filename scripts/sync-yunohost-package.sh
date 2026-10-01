#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(git -C "$script_dir/.." rev-parse --show-toplevel)"
target_dir="${BOOKSHELFNG_YNH_REPO:-$(dirname "$repo_root")/bookshelfng_ynh}"
remote="${BOOKSHELFNG_YNH_REMOTE:-origin}"
branch="${BOOKSHELFNG_YNH_BRANCH:-testing}"

if ! git -C "$repo_root" cat-file -e "HEAD:packaging/yunohost" >/dev/null 2>&1; then
	echo "No committed YunoHost package source exists at packaging/yunohost; cannot sync." >&2
	exit 1
fi

if ! git -C "$repo_root" cat-file -e "HEAD:packaging/yunohost/manifest.toml" >/dev/null 2>&1; then
	echo "The committed YunoHost package source is missing packaging/yunohost/manifest.toml." >&2
	exit 1
fi

if ! git -C "$target_dir" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
	echo "YunoHost package checkout not found at $target_dir; clone it or set BOOKSHELFNG_YNH_REPO." >&2
	exit 1
fi

if ! command -v rsync >/dev/null 2>&1; then
	echo "rsync is required to sync the YunoHost package." >&2
	exit 1
fi

if ! git -C "$target_dir" remote get-url "$remote" >/dev/null 2>&1; then
	echo "Git remote '$remote' is missing from $target_dir." >&2
	exit 1
fi

remote_url="$(git -C "$target_dir" remote get-url "$remote")"
case "$remote_url" in
	*github.com/YunoHost-Apps/bookshelfng_ynh*|*github.com:YunoHost-Apps/bookshelfng_ynh*) ;;
	*)
		echo "Remote '$remote' must point to YunoHost-Apps/bookshelfng_ynh." >&2
		exit 1
		;;
esac

if [[ -n "$(git -C "$target_dir" status --porcelain)" ]]; then
	echo "YunoHost package checkout has local changes; commit or stash them before syncing." >&2
	exit 1
fi

git -C "$target_dir" check-ref-format --branch "$branch" >/dev/null
git -C "$target_dir" fetch "$remote" --prune

if git -C "$target_dir" show-ref --verify --quiet "refs/remotes/$remote/$branch"; then
	if git -C "$target_dir" show-ref --verify --quiet "refs/heads/$branch"; then
		git -C "$target_dir" switch "$branch"
		git -C "$target_dir" merge --ff-only "$remote/$branch"
	else
		git -C "$target_dir" switch --track --create "$branch" "$remote/$branch"
	fi
else
	if ! git -C "$target_dir" show-ref --verify --quiet "refs/remotes/$remote/main"; then
		echo "Remote '$remote' has no main branch to base '$branch' on." >&2
		exit 1
	fi

	if git -C "$target_dir" show-ref --verify --quiet "refs/heads/$branch"; then
		git -C "$target_dir" switch "$branch"
		git -C "$target_dir" merge --ff-only "$remote/main"
	else
		git -C "$target_dir" switch --create "$branch" "$remote/main"
	fi
fi

staging_dir="$(mktemp -d)"
trap 'rm -rf "$staging_dir"' EXIT
git -C "$repo_root" archive --format=tar HEAD packaging/yunohost | tar -xf - -C "$staging_dir"
source_dir="$staging_dir/packaging/yunohost"

# YunoHost regenerates README.md from the manifest and package metadata.
rsync --archive --delete \
	--exclude='/.git/' \
	--exclude='/README.md' \
	"$source_dir/" "$target_dir/"

git -C "$target_dir" add --all
source_commit="$(git -C "$repo_root" rev-parse --short HEAD)"
if ! git -C "$target_dir" diff --cached --quiet; then
	git -C "$target_dir" commit -m "chore: sync YunoHost package from $source_commit"
fi

git -C "$target_dir" push "$remote" "HEAD:refs/heads/$branch"
echo "Synced packaging/yunohost to $target_dir ($branch)."
