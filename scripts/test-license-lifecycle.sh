#!/usr/bin/env bash
# E2E: TestApp + aktywacja/deaktywacja licencji przez GitHub Contents API.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/sample/Ops.Runtime.Seed.TestApp/Ops.Runtime.Seed.TestApp.csproj"
MANIFEST="$ROOT/test-fixtures/manifest.json"
SOURCE_URL="https://mikzielinski.github.io/rpalicense/assets/seed.jwt"

chmod +x "$ROOT/scripts/"*.sh

run_app() {
  local token="${1:-}"
  local label="$2"
  local use_local_catalog="${3:-0}"
  local dll="$ROOT/sample/Ops.Runtime.Seed.TestApp/bin/Release/net6.0/Ops.Runtime.Seed.TestApp.dll"
  local pem="$ROOT/test-fixtures/seal.public.pem"
  local cache
  cache=$(mktemp -d)

  echo ""
  echo "--- $label ---"
  (
    export PATH="$PATH"
    [[ -n "$token" ]] && export FLOW_RUNTIME_TOKEN="$token"
    export OPS_SEED_PUBLIC_SEAL_KEY_FILE="$pem"
    export OPS_SEED_CACHE_PATH="$cache/seed.cache.json"
    export OPS_SEED_PEPPER="test-pepper-ops-runtime-seed-2026"
    export OPS_SEED_ENVELOPE_PEPPER="test-envelope-pepper-ops-runtime-2026"
    export OPS_SEED_ENVELOPE_SIGNING_KEY="test-jwt-signing-key-ops-runtime-seed-2026"
    export OPS_SEED_ENVELOPE_ISSUER="https://mikzielinski.github.io/rpalicense"
    export OPS_SEED_ENVELOPE_AUDIENCE="ops-runtime-seed"
    export OPS_SEED_SOURCE_URL="$SOURCE_URL"
    if [[ "$use_local_catalog" == "1" ]]; then
      export OPS_SEED_CATALOG_FILE="$ROOT/test-fixtures/seed.live.jwt"
    else
      unset OPS_SEED_CATALOG_FILE
    fi
    dotnet "$dll"
  )
  local code=$?
  rm -rf "$cache"
  echo "exit code: $code"
  return $code
}

expect_exit() {
  local expected="$1"
  shift
  local code=0
  "$@" || code=$?
  if [[ "$code" -ne "$expected" ]]; then
    echo "FAIL: oczekiwano exit $expected, otrzymano $code"
    exit 1
  fi
}

echo "========================================"
echo " TestApp — cykl licencji (GitHub API)"
echo "========================================"

echo ""
echo "==> Przygotowanie"
"$ROOT/scripts/generate-test-fixtures.sh" >/dev/null
dotnet build "$APP" -c Release -v q
dotnet build "$ROOT/tests/Ops.Runtime.Seed.Tests/Ops.Runtime.Seed.Tests.csproj" -c Release -v q

TOKEN=$(jq -r '.tokenId' "$MANIFEST")

echo ""
echo "==> Faza 1: BEZ LICENCJI (brak tokenu)"
expect_exit 2 run_app "" "bez tokenu" 1

echo ""
echo "==> Faza 1b: BEZ LICENCJI (niezarejestrowany token)"
expect_exit 1 run_app "RT-NIE-MA-W-KATALOGU" "niezarejestrowany" 1

echo ""
echo "==> Faza 2: AKTYWACJA przez GitHub API"
"$ROOT/scripts/license-api.sh" activate

echo ""
echo "==> Faza 2b: PO NADANIU (TestApp → Pages HTTP)"
expect_exit 0 run_app "$TOKEN" "po nadaniu"

echo ""
echo "==> Faza 3: DEAKTYWACJA przez GitHub API"
"$ROOT/scripts/license-api.sh" deactivate

echo ""
echo "==> Faza 3b: PO DEAKTYWACJI (TestApp → boot-0x12)"
expect_exit 1 run_app "$TOKEN" "po deaktywacji"

echo ""
echo "==> Przywrócenie: AKTYWACJA przez API"
"$ROOT/scripts/license-api.sh" activate

echo ""
echo "==> Testy jednostkowe LicenseLifecycle"
dotnet test "$ROOT/tests/Ops.Runtime.Seed.Tests/Ops.Runtime.Seed.Tests.csproj" -c Release --no-build \
  --filter "FullyQualifiedName~LicenseLifecycle" -v q

echo ""
echo "========================================"
echo " CYKL LICENCJI OK (API + TestApp + Pages)"
echo "  activate   → GitHub Contents API"
echo "  deactivate → GitHub Contents API"
echo "  boot-0x01/0x11 → bez licencji"
echo "  boot-ok-remote → po nadaniu"
echo "  boot-0x12      → po deaktywacji"
echo "========================================"
