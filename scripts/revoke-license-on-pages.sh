#!/usr/bin/env bash
# Revoke license: set enabled=false, reseal, publish disabled seed.jwt to GitHub Pages path.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FIXTURES="$ROOT/test-fixtures"
DISABLED="$FIXTURES/seed.disabled.jwt"
PAGES="$ROOT/docs/assets/seed.jwt"

if [[ ! -f "$DISABLED" ]]; then
  echo "Missing $DISABLED — run scripts/generate-test-fixtures.sh first"
  exit 1
fi

cp "$DISABLED" "$PAGES"
echo "Revoked license published to docs/assets/seed.jwt"

jq -r '.entries[0] | {tokenId, enabled, validToUtc}' "$FIXTURES/catalog/disabled.json"
