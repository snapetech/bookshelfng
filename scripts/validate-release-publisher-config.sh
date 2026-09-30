#!/usr/bin/env bash
set -euo pipefail

trim() {
  local value="$1"
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "$value"
}

validate_discord_webhook() {
  local webhook="${DISCORD_RELEASE_WEBHOOK:-}"
  [[ "$webhook" =~ ^https://(discord\.com|discordapp\.com)/api/webhooks/[0-9]+/[A-Za-z0-9._~-]+/?(\?[A-Za-z0-9._~=%&-]*)?$ ]]
}

if [[ "${1:-publishers}" == discord ]]; then
  if validate_discord_webhook; then
    echo 'Discord release webhook URL format is valid.'
    exit 0
  fi

  echo 'DISCORD_RELEASE_WEBHOOK must be a valid HTTPS Discord webhook URL.' >&2
  exit 1
fi

if [[ "${1:-publishers}" != publishers ]]; then
  echo 'Usage: validate-release-publisher-config.sh [publishers|discord]' >&2
  exit 2
fi

: "${GITHUB_OUTPUT:?GITHUB_OUTPUT must name a writable output file}"
: "${GITHUB_STEP_SUMMARY:?GITHUB_STEP_SUMMARY must name a writable summary file}"

tmp_dir="$(mktemp -d)"
trap 'rm -rf -- "$tmp_dir"' EXIT

aur=false
copr=false
ppa=false
chocolatey=false

skip() {
  printf '%s\n' "- **$1:** skipped. $2" >> "$GITHUB_STEP_SUMMARY"
}

if [[ -z "${AUR_SSH_KEY:-}" ]]; then
  skip AUR 'AUR_SSH_KEY is unset; the AUR publisher will not run.'
else
  aur_key="$tmp_dir/aur-key"
  printf '%s\n' "$AUR_SSH_KEY" > "$aur_key"
  chmod 0600 "$aur_key"
  if ssh-keygen -y -P '' -f "$aur_key" >/dev/null 2>&1; then
    aur=true
  else
    skip AUR 'AUR_SSH_KEY is not a usable unencrypted SSH private key; update the Actions secret.'
  fi
fi

copr_url="$(trim "${COPR_WEBHOOK_URL:-}")"
if [[ -z "$copr_url" ]]; then
  skip COPR 'COPR_WEBHOOK_URL is unset; the COPR publisher will not run.'
elif [[ "$copr_url" =~ ^https://copr\.fedorainfracloud\.org/webhooks/custom/[0-9]+/[A-Za-z0-9._~-]+/bookshelfng/?$ ]]; then
  copr=true
else
  skip COPR 'COPR_WEBHOOK_URL is not a package-scoped COPR custom webhook URL for bookshelfng; replace the Actions secret with the URL from that COPR package’s settings.'
fi

ppa_target="${LAUNCHPAD_PPA:-}"
if [[ -z "${GPG_PRIVATE_KEY:-}" || -z "$ppa_target" ]]; then
  skip PPA 'GPG_PRIVATE_KEY and LAUNCHPAD_PPA are both required; the PPA publisher will not run.'
elif [[ ! "$ppa_target" =~ ^ppa:~?[A-Za-z0-9][A-Za-z0-9.+_-]*/[A-Za-z0-9][A-Za-z0-9.+_-]*$ ]]; then
  skip PPA 'LAUNCHPAD_PPA must use the form ppa:<owner>/<archive>; update the Actions secret or variable.'
else
  gpg_home="$tmp_dir/gnupg"
  mkdir -m 0700 "$gpg_home"
  if ! printf '%s\n' "$GPG_PRIVATE_KEY" | GNUPGHOME="$gpg_home" gpg --batch --import >/dev/null 2>&1 || \
    ! GNUPGHOME="$gpg_home" gpg --batch --with-colons --list-secret-keys 2>/dev/null | awk -F: '$1 == "sec" { found = 1 } END { exit !found }'; then
    skip PPA 'GPG_PRIVATE_KEY is not an importable private signing key; update the Actions secret.'
  else
    shorthand="${ppa_target#ppa:}"
    owner="${shorthand%%/*}"
    archive="${shorthand#*/}"
    owner="${owner#\~}"
    archive_api="https://api.launchpad.net/1.0/~${owner}/%2Barchive/ubuntu/${archive}"
    if curl --fail --silent --show-error --max-time 20 "$archive_api" >/dev/null 2>&1; then
      ppa=true
    else
      skip PPA 'The configured Launchpad PPA could not be found through its public API; check its owner and archive name.'
    fi
  fi
fi

if [[ -z "${CHOCOLATEY_API_KEY:-}" ]]; then
  skip Chocolatey 'CHOCOLATEY_API_KEY is unset; the Chocolatey publisher will not run.'
elif [[ "${CHOCOLATEY_API_KEY}" =~ [[:space:]] ]]; then
  skip Chocolatey 'CHOCOLATEY_API_KEY contains whitespace; update the Actions secret.'
else
  chocolatey=true
fi

{
  printf 'aur=%s\n' "$aur"
  printf 'copr=%s\n' "$copr"
  printf 'ppa=%s\n' "$ppa"
  printf 'chocolatey=%s\n' "$chocolatey"
} >> "$GITHUB_OUTPUT"
