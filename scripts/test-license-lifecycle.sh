#!/usr/bin/env bash
# Test aplikacji bota (sample/Ops.Runtime.Seed.TestApp) w 3 fazach:
#   1) bez licencji
#   2) po nadaniu (live seed.jwt na GitHub Pages)
#   3) po deaktywacji (disabled seed.jwt na Pages)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/sample/Ops.Runtime.Seed.TestApp/Ops.Runtime.Seed.TestApp.csproj"
MANIFEST="$ROOT/test-fixtures/manifest.json"
SOURCE_URL="https://mikzielinski.github.io/rpalicense/assets/seed.jwt"
KEYGEN="dotnet run --project $ROOT/keygen/SeedForge.csproj -c Release --no-build --"

chmod +x "$ROOT/scripts/"*.sh 2>/dev/null || true

run_app() {
  local token="${1:-}"
  local label="$2"
  local dll="$ROOT/sample/Ops.Runtime.Seed.TestApp/bin/Release/net6.0/Ops.Runtime.Seed.TestApp.dll"
  local pem="$ROOT/test-fixtures/seal.public.pem"
  local cache
  cache=$(mktemp -d)

  echo ""
  echo "--- $label ---"
  local env_args=(
    "OPS_SEED_PUBLIC_SEAL_KEY_FILE=$pem"
    "OPS_SEED_CACHE_PATH=$cache/seed.cache.json"
    "OPS_SEED_PEPPER=test-pepper-ops-runtime-seed-2026"
    "OPS_SEED_ENVELOPE_PEPPER=test-envelope-pepper-ops-runtime-2026"
    "OPS_SEED_ENVELOPE_SIGNING_KEY=test-jwt-signing-key-ops-runtime-seed-2026"
    "OPS_SEED_ENVELOPE_ISSUER=https://mikzielinski.github.io/rpalicense"
    "OPS_SEED_ENVELOPE_AUDIENCE=ops-runtime-seed"
    "OPS_SEED_SOURCE_URL=$SOURCE_URL"
  )
  if [[ -n "$token" ]]; then
    env_args+=("FLOW_RUNTIME_TOKEN=$token")
  fi

  set +e
  env -i PATH="$PATH" HOME="$HOME" "${env_args[@]}" \
    dotnet "$dll" 2>&1
  local code=$?
  set -e
  rm -rf "$cache"
  echo "exit code: $code"
  return $code
}

wait_pages_enabled() {
  local expected="$1"
  local tmp
  tmp=$(mktemp)
  for i in $(seq 1 18); do
    if curl -sf "$SOURCE_URL" -o "$tmp" 2>/dev/null; then
      if $KEYGEN unwrapjwt "$tmp" \
        "test-jwt-signing-key-ops-runtime-seed-2026" \
        "test-envelope-pepper-ops-runtime-2026" \
        "https://mikzielinski.github.io/rpalicense" \
        "ops-runtime-seed" 2>/dev/null | jq -e ".entries[0].enabled == $expected" >/dev/null; then
        rm -f "$tmp"
        echo "Pages enabled=$expected (attempt $i)"
        return 0
      fi
    fi
    sleep 10
  done
  rm -f "$tmp"
  return 1
}

echo "========================================"
echo " TestApp — cykl licencji (GitHub Pages)"
echo "========================================"

echo ""
echo "==> Przygotowanie"
"$ROOT/scripts/generate-test-fixtures.sh" | tail -4
dotnet build "$APP" -c Release -v q
dotnet build "$ROOT/tests/Ops.Runtime.Seed.Tests/Ops.Runtime.Seed.Tests.csproj" -c Release -v q

TOKEN=$(jq -r '.tokenId' "$MANIFEST")

echo ""
echo "==> Faza 1: BEZ LICENCJI (brak tokenu)"
run_app "" "bez tokenu" ; test $? -eq 2 || { echo "FAIL: oczekiwano exit 2"; exit 1; }

echo ""
echo "==> Faza 1b: BEZ LICENCJI (niezarejestrowany token)"
run_app "RT-NIE-MA-W-KATALOGU" "niezarejestrowany" ; test $? -eq 1 || { echo "FAIL: oczekiwano exit 1"; exit 1; }

echo ""
echo "==> Przywracanie LIVE JWT na Pages"
"$ROOT/scripts/restore-license-on-pages.sh"
git add "$ROOT/docs/assets/seed.jwt"
git diff --cached --quiet || { git commit -m "Lifecycle test: restore live license on Pages"; git push origin main; }
wait_pages_enabled true

echo ""
echo "==> Faza 2: PO NADANIU (live na Pages)"
run_app "$TOKEN" "po nadaniu" ; test $? -eq 0 || { echo "FAIL: oczekiwano exit 0 boot-ok-remote"; exit 1; }

echo ""
echo "==> Publikacja REVOKED JWT na Pages"
"$ROOT/scripts/revoke-license-on-pages.sh"
git add "$ROOT/docs/assets/seed.jwt"
git diff --cached --quiet || { git commit -m "Lifecycle test: revoke license on Pages"; git push origin main; }
wait_pages_enabled false

echo ""
echo "==> Faza 3: PO DEAKTYWACJI (disabled na Pages)"
run_app "$TOKEN" "po deaktywacji" ; test $? -eq 1 || { echo "FAIL: oczekiwano exit 1 boot-0x12"; exit 1; }

echo ""
echo "==> Przywracanie LIVE JWT"
"$ROOT/scripts/restore-license-on-pages.sh"
git add "$ROOT/docs/assets/seed.jwt"
git diff --cached --quiet || { git commit -m "Lifecycle test: restore live license after revoke test"; git push origin main; }

echo ""
echo "==> Testy jednostkowe cyklu (TestApp subprocess)"
dotnet test "$ROOT/tests/Ops.Runtime.Seed.Tests/Ops.Runtime.Seed.Tests.csproj" -c Release --no-build \
  --filter "FullyQualifiedName~LicenseLifecycle" -v q

echo ""
echo "========================================"
echo " CYKL LICENCJI OK"
echo "  1) bez licencji  → boot-0x01 / boot-0x11"
echo "  2) po nadaniu    → boot-ok-remote"
echo "  3) po deaktywacji → boot-0x12"
echo "========================================"
