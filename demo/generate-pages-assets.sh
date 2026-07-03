#!/usr/bin/env bash
# Generates docs/assets/seed.jwt for GitHub Pages and syncs demo defaults into BootstrapConfig.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="$ROOT/demo/pages-demo.config.json"
ASSETS="$ROOT/docs/assets"
KEYS="$ROOT/demo/keys"
KEYGEN_DLL="$ROOT/keygen/bin/Release/net8.0/SeedForge.dll"

source "${HOME}/.bashrc" 2>/dev/null || true
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

read_cfg() {
  python3 - <<PY
import json
print(json.load(open("$CONFIG"))["$1"])
PY
}

PAGES_BASE="$(read_cfg pagesBaseUrl)"
SEED_PATH="$(read_cfg seedPath)"
TOKEN_ID="$(read_cfg tokenId)"
OWNER="$(read_cfg owner)"
VALID_TO="$(read_cfg validToUtc)"
PEPPER="$(read_cfg pepper)"
ENV_PEPPER="$(read_cfg envelopePepper)"
JWT_KEY="$(read_cfg envelopeSigningKey)"
ISSUER="$(read_cfg envelopeIssuer)"
AUD="$(read_cfg envelopeAudience)"
SOURCE_URL="${PAGES_BASE}${SEED_PATH}"

echo "Building keygen..."
(cd "$ROOT/keygen" && dotnet restore -p:TargetFramework=net8.0 >/dev/null && dotnet build -c Release -p:TargetFramework=net8.0 --no-restore >/dev/null)

mkdir -p "$ASSETS" "$KEYS"

if [[ ! -f "$KEYS/seal.private.pem" ]]; then
  echo "Generating RSA keys (demo/keys — gitignored)..."
  dotnet "$KEYGEN_DLL" newkeys "$KEYS"
fi

cp "$KEYS/seal.public.pem" "$ASSETS/seal.public.pem"

echo "Issuing catalog entry (any machine allowed)..."
dotnet "$KEYGEN_DLL" issue \
  "$KEYS/seal.private.pem" \
  "$PEPPER" \
  "$TOKEN_ID" \
  "$OWNER" \
  "$VALID_TO" \
  "" \
  "$ROOT/sample/payload.example.json" > "$ASSETS/entry.json"

python3 - <<PY
import json
entry = json.load(open("$ASSETS/entry.json"))
json.dump({"entries": [entry]}, open("$ASSETS/catalog.json", "w"), ensure_ascii=False, indent=2)
PY

echo "Wrapping seed.jwt..."
dotnet "$KEYGEN_DLL" wrapjwt \
  "$ASSETS/catalog.json" \
  "$JWT_KEY" \
  "$ENV_PEPPER" \
  "$ISSUER" \
  "$AUD" \
  "$VALID_TO" > "$ASSETS/seed.jwt"

python3 - <<PY
import json, pathlib
cfg = json.load(open("$CONFIG"))
env = pathlib.Path("$ROOT/demo/pages-demo.env")
env.write_text(f"""# Source before running demo/Ops.Runtime.Seed.AutoInitDemo
export FLOW_RUNTIME_SOURCE_URL={cfg['pagesBaseUrl']}{cfg['seedPath']}
export FLOW_RUNTIME_PEPPER={cfg['pepper']}
export FLOW_RUNTIME_ENVELOPE_PEPPER={cfg['envelopePepper']}
export FLOW_RUNTIME_ENVELOPE_SIGNING_KEY={cfg['envelopeSigningKey']}
export FLOW_RUNTIME_ENVELOPE_ISSUER={cfg['envelopeIssuer']}
export FLOW_RUNTIME_ENVELOPE_AUDIENCE={cfg['envelopeAudience']}
export FLOW_RUNTIME_PUBLIC_SEAL_KEY_PEM_FILE={cfg.get('publicKeyPath', '$ASSETS/seal.public.pem')}
export FLOW_RUNTIME_TOKEN={cfg['tokenId']}
export APP_BOOT_TOKEN={cfg['tokenId']}
""", encoding="utf-8")
print("Wrote demo/pages-demo.env")
PY

echo "Done."
echo "  seed.jwt  -> $ASSETS/seed.jwt"
echo "  Pages URL -> $SOURCE_URL"
wc -c "$ASSETS/seed.jwt"
