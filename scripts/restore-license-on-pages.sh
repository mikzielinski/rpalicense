#!/usr/bin/env bash
# Restore live license: publish enabled seed.jwt back to GitHub Pages path.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FIXTURES="$ROOT/test-fixtures"
LIVE="$FIXTURES/seed.live.jwt"
PAGES="$ROOT/docs/assets/seed.jwt"

if [[ ! -f "$LIVE" ]]; then
  echo "Missing $LIVE — run scripts/generate-test-fixtures.sh first"
  exit 1
fi

cp "$LIVE" "$PAGES"
echo "Live license restored to docs/assets/seed.jwt"

jq -r '.entries[0] | {tokenId, enabled, validToUtc}' "$FIXTURES/catalog/live.json"
