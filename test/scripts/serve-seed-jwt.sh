#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FIXTURES="$ROOT/test/fixtures"
PORT="${SEED_JWT_PORT:-8765}"
VARIANT="${SEED_JWT_VARIANT:-valid}"

if [[ ! -f "$FIXTURES/seed.${VARIANT}.jwt" ]]; then
  echo "Missing $FIXTURES/seed.${VARIANT}.jwt — run test/scripts/generate-fixtures.sh first" >&2
  exit 1
fi

mkdir -p "$FIXTURES/serve"
cp "$FIXTURES/seed.${VARIANT}.jwt" "$FIXTURES/serve/seed.jwt"

echo "Serving seed.jwt (${VARIANT}) at http://127.0.0.1:${PORT}/seed.jwt"
echo "Press Ctrl+C to stop."
cd "$FIXTURES/serve"
python3 -m http.server "$PORT"
