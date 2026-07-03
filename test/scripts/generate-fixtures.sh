#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FIXTURES="$ROOT/test/fixtures"
KEYGEN_DLL="$ROOT/keygen/bin/Release/net8.0/SeedForge.dll"
LIB_DIR="$ROOT/src/Ops.Runtime.Seed"

source "${HOME}/.bashrc" 2>/dev/null || true
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

PEPPER="${FLOW_RUNTIME_PEPPER:-demo-long-pepper-value-123456}"
JWT_KEY="${FLOW_RUNTIME_ENVELOPE_SIGNING_KEY:-demo-long-jwt-signing-key-abcdef}"
ENV_PEPPER="${FLOW_RUNTIME_ENVELOPE_PEPPER:-demo-long-envelope-pepper-xyz789}"
ISSUER="${FLOW_RUNTIME_ENVELOPE_ISSUER:-https://example.github.io}"
AUD="${FLOW_RUNTIME_ENVELOPE_AUDIENCE:-ops-runtime-seed}"
TOKEN_ID="${FLOW_RUNTIME_TEST_TOKEN:-RT-2026-CLIENT-001}"
MACHINE="${FLOW_RUNTIME_TEST_MACHINE:-ROBOT01}"
VALID_TO="${FLOW_RUNTIME_TEST_VALID_TO:-2026-12-31T23:59:59Z}"
EXPIRED_TO="${FLOW_RUNTIME_TEST_EXPIRED_TO:-2020-01-01T00:00:00Z}"

echo "Building keygen + library..."
(cd "$ROOT/keygen" && dotnet restore -p:TargetFramework=net8.0 >/dev/null && dotnet build -c Release -p:TargetFramework=net8.0 --no-restore >/dev/null)
(cd "$LIB_DIR" && dotnet build -c Release -p:LangVersion=latest >/dev/null)

rm -rf "$FIXTURES"
mkdir -p "$FIXTURES/keys" "$FIXTURES/cache" "$FIXTURES/catalogs"

echo "Generating RSA keys..."
dotnet "$KEYGEN_DLL" newkeys "$FIXTURES/keys"

issue_entry() {
  local token_id="$1"
  local owner="$2"
  local valid_to="$3"
  local enabled="$4"
  local hosts="$5"
  local out="$6"
  dotnet "$KEYGEN_DLL" issue \
    "$FIXTURES/keys/seal.private.pem" \
    "$PEPPER" \
    "$token_id" \
    "$owner" \
    "$valid_to" \
    "$hosts" \
    "$ROOT/sample/payload.example.json" > "$out.entry.json"
  python3 - <<PY
import json
entry = json.load(open("$out.entry.json"))
entry["enabled"] = $( [[ "$enabled" == "true" ]] && echo "True" || echo "False" )
json.dump({"entries": [entry]}, open("$out.json", "w"), ensure_ascii=False, indent=2)
PY
}

wrap_catalog() {
  local catalog="$1"
  local out="$2"
  dotnet "$KEYGEN_DLL" wrapjwt \
    "$catalog" \
    "$JWT_KEY" \
    "$ENV_PEPPER" \
    "$ISSUER" \
    "$AUD" \
    "$VALID_TO" > "$out"
}

echo "Building catalogs + seed.jwt variants..."
issue_entry "$TOKEN_ID" "Klient Sp. z o.o." "$VALID_TO" true "$MACHINE,ROBOT02" "$FIXTURES/catalogs/valid"
wrap_catalog "$FIXTURES/catalogs/valid.json" "$FIXTURES/seed.valid.jwt"

issue_entry "$TOKEN_ID" "Klient Sp. z o.o." "$VALID_TO" false "$MACHINE,ROBOT02" "$FIXTURES/catalogs/disabled"
wrap_catalog "$FIXTURES/catalogs/disabled.json" "$FIXTURES/seed.disabled.jwt"

issue_entry "$TOKEN_ID" "Klient Sp. z o.o." "$EXPIRED_TO" true "$MACHINE,ROBOT02" "$FIXTURES/catalogs/expired"
wrap_catalog "$FIXTURES/catalogs/expired.json" "$FIXTURES/seed.expired.jwt"

issue_entry "$TOKEN_ID" "Klient Sp. z o.o." "$VALID_TO" true "$MACHINE,ROBOT02" "$FIXTURES/catalogs/bad-seal"
python3 - <<PY
import json
path = "$FIXTURES/catalogs/bad-seal.json"
doc = json.load(open(path))
doc["entries"][0]["seal"] = "invalid"
json.dump(doc, open(path, "w"), ensure_ascii=False, indent=2)
PY
wrap_catalog "$FIXTURES/catalogs/bad-seal.json" "$FIXTURES/seed.bad-seal.jwt"

cat > "$FIXTURES/metadata.json" <<JSON
{
  "validToken": "$TOKEN_ID",
  "allowedMachine": "$MACHINE",
  "pepper": "$PEPPER",
  "envelopePepper": "$ENV_PEPPER",
  "envelopeSigningKey": "$JWT_KEY",
  "envelopeIssuer": "$ISSUER",
  "envelopeAudience": "$AUD"
}
JSON

cat > "$FIXTURES/runtime.env" <<ENV
# Source these before running the UiPath robot or test harness on Mac/Linux.
export FLOW_RUNTIME_PEPPER="$PEPPER"
export FLOW_RUNTIME_ENVELOPE_PEPPER="$ENV_PEPPER"
export FLOW_RUNTIME_ENVELOPE_SIGNING_KEY="$JWT_KEY"
export FLOW_RUNTIME_ENVELOPE_ISSUER="$ISSUER"
export FLOW_RUNTIME_ENVELOPE_AUDIENCE="$AUD"
export FLOW_RUNTIME_PUBLIC_SEAL_KEY_PEM_FILE="$FIXTURES/keys/seal.public.pem"
export FLOW_RUNTIME_SOURCE_URL="http://127.0.0.1:8765/seed.jwt"
export FLOW_RUNTIME_TEST_TOKEN="$TOKEN_ID"
export FLOW_RUNTIME_TEST_MACHINE="$MACHINE"
ENV

echo "Fixtures ready in $FIXTURES"
ls -la "$FIXTURES"
