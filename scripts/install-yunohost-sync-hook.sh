#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
hook_names=(post-commit pre-push)
sync_scripts=(
	"$repo_root/scripts/sync-yunohost-package.sh"
	"$repo_root/scripts/sync-unraid-package.sh"
)
local_hooks_dir="$(git -C "$repo_root" config --local --path core.hooksPath || true)"
effective_hooks_dir="$(git -C "$repo_root" config --path core.hooksPath || true)"

if [[ -n "$effective_hooks_dir" && -z "$local_hooks_dir" ]]; then
	echo "A shared core.hooksPath is configured. Set a repository-local core.hooksPath before installing the package sync hook." >&2
	exit 1
fi

hooks_dir="$local_hooks_dir"
if [[ -z "$hooks_dir" ]]; then
	hooks_dir="$(git -C "$repo_root" rev-parse --git-path hooks)"
fi

if [[ "$hooks_dir" != /* ]]; then
	hooks_dir="$repo_root/$hooks_dir"
fi

mkdir -p "$hooks_dir"

for sync_script in "${sync_scripts[@]}"; do
	if [[ ! -f "$sync_script" ]]; then
		echo "Missing package sync script: $sync_script" >&2
		exit 1
	fi
	chmod +x "$sync_script"
done

for hook_name in "${hook_names[@]}"; do
	source_hook="$repo_root/.githooks/$hook_name"
	hook_path="$hooks_dir/$hook_name"
	if [[ ! -f "$source_hook" ]]; then
		echo "Missing package sync hook: $source_hook" >&2
		exit 1
	fi

	chmod +x "$source_hook"
	if [[ -e "$hook_path" || -L "$hook_path" ]]; then
		if [[ ! "$hook_path" -ef "$source_hook" ]]; then
			echo "Refusing to replace an existing $hook_name hook: $hook_path" >&2
			exit 1
		fi
	fi
done

for hook_name in "${hook_names[@]}"; do
	source_hook="$repo_root/.githooks/$hook_name"
	hook_path="$hooks_dir/$hook_name"
	if [[ -e "$hook_path" || -L "$hook_path" ]]; then
		echo "BookshelfNG $hook_name package sync hook is already installed."
	else
		ln -s "$source_hook" "$hook_path"
		echo "Installed BookshelfNG $hook_name package sync hook at $hook_path"
	fi
done
