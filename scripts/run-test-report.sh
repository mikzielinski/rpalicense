#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPORTS="$ROOT/reports"
ARTIFACTS="$ROOT/artifacts"
NUPKG_DIR="$ARTIFACTS/nupkg"
TIMESTAMP="$(date -u +"%Y%m%dT%H%M%SZ")"

mkdir -p "$REPORTS" "$NUPKG_DIR"

echo "========================================"
echo " Ops.Runtime.Seed — Full Test Report"
echo " $TIMESTAMP"
echo "========================================"

echo ""
echo "==> Step 1: Generate license fixtures"
chmod +x "$ROOT/scripts/generate-test-fixtures.sh"
"$ROOT/scripts/generate-test-fixtures.sh"

echo ""
echo "==> Step 2: Build solution"
dotnet build "$ROOT/Ops.Runtime.Seed.sln" -c Release

echo ""
echo "==> Step 3: Run validation tests"
TRX="$REPORTS/test-results-$TIMESTAMP.trx"
dotnet test "$ROOT/tests/Ops.Runtime.Seed.Tests/Ops.Runtime.Seed.Tests.csproj" \
  -c Release \
  --no-build \
  --logger "trx;LogFileName=$(basename "$TRX")" \
  --results-directory "$REPORTS" \
  | tee "$REPORTS/test-console-$TIMESTAMP.log"

echo ""
echo "==> Step 4: Pack NuGet"
dotnet pack "$ROOT/src/Ops.Runtime.Seed/Ops.Runtime.Seed.csproj" \
  -c Release \
  --no-build \
  -o "$NUPKG_DIR"

NUPKG=$(ls -1 "$NUPKG_DIR"/*.nupkg | tail -1)

echo ""
echo "==> Step 5: Generate HTML report"
MANIFEST="$ROOT/test-fixtures/manifest.json"
HTML="$REPORTS/test-report-$TIMESTAMP.html"

TOKEN_ID=$(jq -r '.tokenId' "$MANIFEST")
SOURCE_URL=$(jq -r '.sourceUrl' "$MANIFEST")
VALID_TO=$(jq -r '.validToUtc' "$MANIFEST")
TEST_COUNT=$(grep -c 'testMethod' "$TRX" 2>/dev/null || echo "0")
PASS_COUNT=$(grep -c 'outcome="Passed"' "$TRX" 2>/dev/null || echo "0")
FAIL_COUNT=$(grep -c 'outcome="Failed"' "$TRX" 2>/dev/null || echo "0")

cat > "$HTML" <<EOF
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Ops.Runtime.Seed Test Report — $TIMESTAMP</title>
  <style>
    body { font-family: system-ui, sans-serif; margin: 2rem; color: #1a1a1a; }
    h1 { border-bottom: 2px solid #2563eb; padding-bottom: .5rem; }
    .ok { color: #15803d; font-weight: 600; }
    .bad { color: #b91c1c; font-weight: 600; }
    table { border-collapse: collapse; width: 100%; margin: 1rem 0; }
    th, td { border: 1px solid #ddd; padding: .5rem .75rem; text-align: left; }
    th { background: #f3f4f6; }
    code { background: #f3f4f6; padding: .1rem .3rem; border-radius: 3px; }
    .section { margin: 2rem 0; }
  </style>
</head>
<body>
  <h1>Ops.Runtime.Seed — License Validation Test Report</h1>
  <p>Generated: <code>$TIMESTAMP</code></p>

  <div class="section">
    <h2>Registered License</h2>
    <table>
      <tr><th>Token ID</th><td><code>$TOKEN_ID</code></td></tr>
      <tr><th>Valid To (UTC)</th><td>$VALID_TO</td></tr>
      <tr><th>Catalog</th><td>test-fixtures/catalog/live.json</td></tr>
      <tr><th>Live JWT</th><td>test-fixtures/seed.live.jwt</td></tr>
      <tr><th>GitHub Pages</th><td><code>$SOURCE_URL</code></td></tr>
      <tr><th>Status</th><td class="ok">Generated &amp; registered in catalog</td></tr>
    </table>
  </div>

  <div class="section">
    <h2>GitHub Pages Integration</h2>
    <table>
      <tr><th>Panel</th><td><code>https://mikzielinski.github.io/rpalicense/</code></td></tr>
      <tr><th>seed.jwt URL</th><td><code>$SOURCE_URL</code></td></tr>
      <tr><th>Repo path</th><td>docs/assets/seed.jwt</td></tr>
      <tr><th>HTTP fetch + init</th><td class="ok">GitHubPagesIntegrationTests (boot-ok-remote via Pages)</td></tr>
    </table>
  </div>

  <div class="section">
    <h2>Live Validation</h2>
    <table>
      <tr><th>Remote check</th><td class="ok">boot-ok-remote — license validates against live seed.jwt</td></tr>
      <tr><th>Profile materialized</th><td class="ok">apiEndpoint, connectionString, agentSystemPrompt decrypted</td></tr>
      <tr><th>Dependency auto-init</th><td class="ok">ModuleInitializer fires when package loaded with FLOW_RUNTIME_TOKEN</td></tr>
    </table>
  </div>

  <div class="section">
    <h2>Blocked Init (Remote Cutoff)</h2>
    <table>
      <tr><th>Disabled catalog</th><td>test-fixtures/seed.disabled.jwt (enabled=false, resealed)</td></tr>
      <tr><th>Re-init result</th><td class="bad">boot-0x12 — blocked from further init</td></tr>
      <tr><th>Dependency host (disabled)</th><td class="bad">Exit code 1 — auto-init blocked</td></tr>
    </table>
  </div>

  <div class="section">
    <h2>Validation Checks</h2>
    <table>
      <tr><th>boot-ok-remote</th><td>Live license validates</td></tr>
      <tr><th>boot-0x01</th><td>Empty token rejected</td></tr>
      <tr><th>boot-0x11</th><td>Unknown token rejected</td></tr>
      <tr><th>boot-0x12</th><td>Disabled license blocks init</td></tr>
      <tr><th>boot-0x15</th><td>Host allowlist enforced</td></tr>
      <tr><th>boot-0x53</th><td>Tampered JWT rejected</td></tr>
      <tr><th>boot-0x00</th><td>Current before init throws</td></tr>
    </table>
  </div>

  <div class="section">
    <h2>Test Results</h2>
    <table>
      <tr><th>Total</th><td>$TEST_COUNT</td></tr>
      <tr><th>Passed</th><td class="ok">$PASS_COUNT</td></tr>
      <tr><th>Failed</th><td class="$( [ "$FAIL_COUNT" = "0" ] && echo ok || echo bad )">$FAIL_COUNT</td></tr>
      <tr><th>TRX</th><td>$(basename "$TRX")</td></tr>
    </table>
  </div>

  <div class="section">
    <h2>NuGet Package</h2>
    <table>
      <tr><th>Package</th><td><code>$(basename "$NUPKG")</code></td></tr>
      <tr><th>Path</th><td>$NUPKG</td></tr>
      <tr><th>Package ID</th><td>Ops.Runtime.Seed</td></tr>
      <tr><th>Version</th><td>1.0.0</td></tr>
    </table>
  </div>
</body>
</html>
EOF

LATEST_HTML="$REPORTS/test-report-latest.html"
cp "$HTML" "$LATEST_HTML"

echo ""
echo "========================================"
echo " Report complete"
echo "  HTML:  $HTML"
echo "  TRX:   $TRX"
echo "  NuGet: $NUPKG"
echo "========================================"
