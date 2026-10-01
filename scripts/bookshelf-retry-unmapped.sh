#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Queue a catalog retry for unmapped BookshelfNG library files.

Usage:
  bookshelf-retry-unmapped.sh --url URL [--api-key KEY] [--wait] [--json]

Environment:
  BOOKSHELF_URL       BookshelfNG base URL, including any URL base path
  BOOKSHELF_API_KEY   API key (prefer this over --api-key)

Options:
  --url URL       BookshelfNG base URL; may end in /api/v1
  --api-key KEY   API key sent in the X-Api-Key header
  --wait          Wait for completion and print the final command result
  --json          Print the API response as JSON
  -h, --help      Show this help
USAGE
}

base_url="${BOOKSHELF_URL:-}"
api_key="${BOOKSHELF_API_KEY:-}"
wait_for_completion=false
json_output=false

while (($#)); do
  case "$1" in
    --url)
      (($# >= 2)) || { usage >&2; exit 2; }
      base_url="$2"
      shift 2
      ;;
    --api-key)
      (($# >= 2)) || { usage >&2; exit 2; }
      api_key="$2"
      shift 2
      ;;
    --wait)
      wait_for_completion=true
      shift
      ;;
    --json)
      json_output=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      printf 'Unknown option: %s\n' "$1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ -z "$base_url" || -z "$api_key" ]]; then
  printf 'Set BOOKSHELF_URL and BOOKSHELF_API_KEY, or pass --url and --api-key.\n' >&2
  exit 2
fi

if ! command -v curl >/dev/null 2>&1; then
  printf 'curl is required.\n' >&2
  exit 2
fi

if [[ "$wait_for_completion" == true ]] && ! command -v jq >/dev/null 2>&1; then
  printf 'jq is required with --wait.\n' >&2
  exit 2
fi

base_url="${base_url%/}"
if [[ "$base_url" != */api/v1 ]]; then
  api_url="$base_url/api/v1"
else
  api_url="$base_url"
fi

command_response="$(curl --fail --silent --show-error \
  --request POST \
  --header "X-Api-Key: $api_key" \
  --header 'Content-Type: application/json' \
  --data '{"name":"RetryUnmappedFiles"}' \
  "$api_url/command")"

if [[ "$wait_for_completion" != true ]]; then
  if [[ "$json_output" == true ]]; then
    printf '%s\n' "$command_response"
  elif command -v jq >/dev/null 2>&1; then
    command_id="$(jq -r '.id // empty' <<<"$command_response")"
    if [[ -n "$command_id" ]]; then
      printf 'Queued unmapped-file catalog retry as command %s.\n' "$command_id"
    else
      printf '%s\n' "$command_response"
    fi
  else
    printf 'Queued unmapped-file catalog retry. API response: %s\n' "$command_response"
  fi
  exit 0
fi

command_id="$(jq -er '.id // empty' <<<"$command_response")"
if [[ -z "$command_id" ]]; then
  printf 'The API accepted the request but returned no command id: %s\n' "$command_response" >&2
  exit 1
fi

while true; do
  command_response="$(curl --fail --silent --show-error \
    --header "X-Api-Key: $api_key" \
    "$api_url/command/$command_id")"
  status="$(jq -r '.status // empty' <<<"$command_response")"

  case "$status" in
    completed|failed|aborted|cancelled)
      break
      ;;
  esac

  sleep 2
done

if [[ "$json_output" == true ]]; then
  printf '%s\n' "$command_response"
else
  printf 'Unmapped-file retry %s.\n' "$status"
  jq -r '.message // .result // empty' <<<"$command_response"
fi

[[ "$status" == completed ]]
