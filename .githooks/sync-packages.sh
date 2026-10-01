#!/usr/bin/env bash
set -uo pipefail

trigger="${1:-manual}"
repo_root="$(git rev-parse --show-toplevel)"
overall_status=0

# Git exports the current repository context to hooks. Clear it before the
# package scripts run Git commands against their separate checkouts.
while IFS= read -r git_environment_variable; do
	unset "$git_environment_variable"
done < <(git -C "$repo_root" rev-parse --local-env-vars)

echo "Running YunoHost and Unraid package sync ($trigger)."

for script in sync-yunohost-package.sh sync-unraid-package.sh; do
	sync_script="$repo_root/scripts/$script"
	if [[ ! -f "$sync_script" ]]; then
		echo "Package sync script not found: $sync_script" >&2
		overall_status=1
		continue
	fi

	if bash "$sync_script"; then
		:
	else
		sync_status=$?
		echo "Package sync failed: $script (exit $sync_status)." >&2
		overall_status=1
	fi
done

if [[ "$overall_status" -ne 0 ]]; then
	echo "Package sync did not complete; $trigger is failing." >&2
fi

exit "$overall_status"
