#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sdk_root="${1:-$repo_root/.sdk}"
lock_file="$repo_root/eng/sdrsharp-sdk.lock.json"
archive="$(mktemp "${TMPDIR:-/tmp}/sdrnexus-sdk.XXXXXX.zip")"
extract_root="$(mktemp -d "${TMPDIR:-/tmp}/sdrnexus-sdk.XXXXXX")"

cleanup() {
  rm -f "$archive"
  rm -rf "$extract_root"
}
trap cleanup EXIT

sdk_url="$(node -e 'const x=require(process.argv[1]); process.stdout.write(x.source)' "$lock_file")"
sdk_sha="$(node -e 'const x=require(process.argv[1]); process.stdout.write(x.sha256)' "$lock_file")"

curl --fail --location --silent --show-error "$sdk_url" --output "$archive"

if command -v sha256sum >/dev/null 2>&1; then
  actual_sha="$(sha256sum "$archive" | awk '{print $1}')"
else
  actual_sha="$(shasum -a 256 "$archive" | awk '{print $1}')"
fi

if [[ "$actual_sha" != "$sdk_sha" ]]; then
  echo "SDR# SDK checksum mismatch." >&2
  echo "Expected: $sdk_sha" >&2
  echo "Actual:   $actual_sha" >&2
  exit 1
fi

unzip -q "$archive" 'sdrplugins/lib/SDRSharp.Common.dll' 'sdrplugins/lib/SDRSharp.PanView.dll' 'sdrplugins/lib/SDRSharp.Radio.dll' -d "$extract_root"
mkdir -p "$sdk_root"
cp "$extract_root"/sdrplugins/lib/SDRSharp.*.dll "$sdk_root"/

echo "Verified SDR# SDK reference assemblies installed at $sdk_root"

