#!/usr/bin/env bash
# Proves the single-holder invariant on a real machine: every refresh token
# across the live credential file and every parked profile exists in exactly
# one file. Prints `files=N distinct=N duplicates=0` and exits 0; any duplicate
# lineage is listed and the exit code is 1. Reads the files, prints only
# SHA-256 fingerprints, never a token.
#
# Usage: check-single-holder.sh <profiles-root> <live-dir>
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <profiles-root> <live-dir>" >&2
  exit 2
fi

profiles_root="$1"
live_dir="$2"
credential_file=".credentials.json"

fingerprint() {
  # jq -j prints the raw token without a trailing newline; only its hash leaves this function.
  jq -j '.claudeAiOauth.refreshToken // empty' -- "$1" | sha256sum | cut -c1-64
}

declare -a files=()
[[ -f "$live_dir/$credential_file" ]] && files+=("$live_dir/$credential_file")
if [[ -d "$profiles_root" ]]; then
  while IFS= read -r -d '' file; do
    files+=("$file")
  done < <(find "$profiles_root" -mindepth 2 -maxdepth 2 -name "$credential_file" -type f -print0 | sort -z)
fi

declare -A holders=()
duplicates=0
for file in "${files[@]}"; do
  hash="$(fingerprint "$file")"
  if [[ -z "$hash" || "$hash" == "$(printf '' | sha256sum | cut -c1-64)" ]]; then
    echo "no refresh token in $file" >&2
    continue
  fi
  if [[ -n "${holders[$hash]:-}" ]]; then
    duplicates=$((duplicates + 1))
    echo "duplicate lineage ${hash:0:12}: ${holders[$hash]} and $file" >&2
  else
    holders[$hash]="$file"
  fi
done

echo "files=${#files[@]} distinct=${#holders[@]} duplicates=$duplicates"
[[ "$duplicates" -eq 0 ]]
