#!/usr/bin/env bash
# End-to-end revoke check: live OK → revoke on Pages → boot-0x12 → restore live.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE_URL="https://mikzielinski.github.io/rpalicense/assets/seed.jwt"
KEYGEN="dotnet run --project $ROOT/keygen/SeedForge.csproj -c Release --no-build --"

chmod +x "$ROOT/scripts/"*.sh

wait_for_pages_enabled() {
  local expected="$1"
  local tmp
  tmp=$(mktemp)
  for i in $(seq 1 12); do
    if curl -sf "$SOURCE_URL" -o "$tmp" 2>/dev/null; then
      if $KEYGEN unwrapjwt "$tmp" \
        "test-jwt-signing-key-ops-runtime-seed-2026" \
        "test-envelope-pepper-ops-runtime-2026" \
        "https://mikzielinski.github.io/rpalicense" \
        "ops-runtime-seed" 2>/dev/null | jq -e ".entries[0].enabled == $expected" >/dev/null; then
        rm -f "$tmp"
        echo "    Pages enabled=$expected (attempt $i)"
        return 0
      fi
    fi
    echo "    attempt $i: waiting for enabled=$expected..."
    sleep 10
  done
  rm -f "$tmp"
  return 1
}

echo "========================================"
echo " License Revoke Check (GitHub Pages)"
echo "========================================"

echo ""
echo "==> [1/7] Ensure fixtures exist"
"$ROOT/scripts/generate-test-fixtures.sh" | tail -6

echo ""
echo "==> [2/7] Verify LIVE license on Pages (pre-revoke)"
dotnet build "$ROOT/tests/Ops.Runtime.Seed.Tests/Ops.Runtime.Seed.Tests.csproj" -c Release -v q
dotnet test "$ROOT/tests/Ops.Runtime.Seed.Tests/Ops.Runtime.Seed.Tests.csproj" -c Release --no-build \
  --filter "FullyQualifiedName~GitHubPages_LiveLicense" -v q
echo "    LIVE: boot-ok-remote OK"

echo ""
echo "==> [3/7] Revoke: publish disabled seed.jwt"
"$ROOT/scripts/revoke-license-on-pages.sh"

echo ""
echo "==> [4/7] Commit & push revoked JWT to main"
cd "$ROOT"
git add docs/assets/seed.jwt
git diff --cached --quiet && echo "    nothing to commit" || {
  git commit -m "Revoke test license RT-TEST-REPORT-001 on GitHub Pages (enabled=false)"
  git push origin main
}

echo ""
echo "==> [5/7] Wait for Pages to serve revoked JWT"
wait_for_pages_enabled false

echo ""
echo "==> [6/7] Run revocation integration tests against live Pages"
dotnet test "$ROOT/tests/Ops.Runtime.Seed.Tests/Ops.Runtime.Seed.Tests.csproj" -c Release --no-build \
  --filter "FullyQualifiedName~GitHubPagesRevocation" -v q
echo "    REVOKED: boot-0x12 OK"

echo ""
echo "==> [7/7] Restore live license on Pages"
"$ROOT/scripts/restore-license-on-pages.sh"
git add docs/assets/seed.jwt
git diff --cached --quiet || {
  git commit -m "Restore live test license RT-TEST-REPORT-001 on GitHub Pages"
  git push origin main
}
wait_for_pages_enabled true || echo "    (restore propagating — run live tests manually if needed)"

echo ""
echo "========================================"
echo " Revoke check complete"
echo "  Revoked:  boot-0x12 (blocked init)"
echo "  Restored: live seed.jwt pushed back"
echo "========================================"
