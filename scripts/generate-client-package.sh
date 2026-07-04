#!/usr/bin/env bash
# Generator paczki NuGet per klient (sposób B): token XOR w DLL, bez env w UiPath.
#
# Usage:
#   ./scripts/generate-client-package.sh RT-2026-CLIENT-001
#   ./scripts/generate-client-package.sh RT-2026-CLIENT-001 --client-name "Acme Sp. z o.o."
#   ./scripts/generate-client-package.sh RT-2026-CLIENT-001 --output-dir dist/clients
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOKEN=""
CLIENT_NAME=""
OUTPUT_ROOT="$ROOT/dist/clients"
SKIP_VERIFY=false

cleanup() {
  "$ROOT/scripts/embed-runtime-credential.sh" "" >/dev/null 2>&1 || true
}
trap cleanup EXIT

while [ $# -gt 0 ]; do
  case "$1" in
    --client-name)
      CLIENT_NAME="${2:-}"
      shift 2
      ;;
    --output-dir)
      OUTPUT_ROOT="${2:-}"
      shift 2
      ;;
    --skip-verify)
      SKIP_VERIFY=true
      shift
      ;;
    -h|--help)
      sed -n '2,12p' "$0"
      exit 0
      ;;
    RT-*)
      TOKEN="$1"
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [ -z "$TOKEN" ]; then
  echo "Usage: $0 RT-YYYY-CLIENT-NNN [--client-name NAME] [--output-dir DIR]" >&2
  exit 1
fi

if ! [[ "$TOKEN" =~ ^RT-[A-Z0-9-]+$ ]]; then
  echo "Invalid token format: $TOKEN (expected RT-...)" >&2
  exit 1
fi

TOKEN_SLUG=$(echo "$TOKEN" | tr '[:upper:]' '[:lower:]')
CLIENT_DIR="$OUTPUT_ROOT/$TOKEN_SLUG"
BUILD_DIR="$ROOT/artifacts/client-build/$TOKEN_SLUG"
DLL_SRC="$ROOT/src/Ops.Runtime.Seed/bin/Release/net6.0/Ops.Runtime.Seed.dll"
NUPKG_NAME="Ops.Runtime.Seed.1.0.0.nupkg"

mkdir -p "$CLIENT_DIR" "$BUILD_DIR"

echo "==> Embed runtime credential"
"$ROOT/scripts/embed-runtime-credential.sh" "$TOKEN"

if [ "$SKIP_VERIFY" = false ]; then
  echo "==> Verify XOR roundtrip"
  DECODED=$(python3 - "$ROOT/src/Ops.Runtime.Seed/RuntimeCredential.Payload.g.cs" <<'PY'
import re, sys
path = sys.argv[1]
text = open(path, encoding="utf-8").read()
m = re.search(r"new byte\[\]\s*\{\s*([^}]*)\s*\}", text)
if not m or not m.group(1).strip():
    print("", end="")
    sys.exit(0)
payload = [int(x.strip()) for x in m.group(1).split(",") if x.strip()]
mask = b"OpsRuntimeSeed2026"
decoded = "".join(chr(b ^ mask[i % len(mask)]) for i, b in enumerate(payload))
print(decoded, end="")
PY
)
  if [ "$DECODED" != "$TOKEN" ]; then
    echo "Embed verification failed." >&2
    exit 1
  fi
fi

echo "==> Build Release + pack"
dotnet build "$ROOT/src/Ops.Runtime.Seed/Ops.Runtime.Seed.csproj" -c Release -v q
dotnet pack "$ROOT/src/Ops.Runtime.Seed/Ops.Runtime.Seed.csproj" -c Release --no-build -o "$BUILD_DIR" -v q

cp "$BUILD_DIR/$NUPKG_NAME" "$CLIENT_DIR/$NUPKG_NAME"
cp "$DLL_SRC" "$CLIENT_DIR/Ops.Runtime.Seed.dll"

BUILT_AT=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
MANIFEST_NAME=${CLIENT_NAME:-$TOKEN}

python3 - "$CLIENT_DIR/manifest.json" "$TOKEN" "$MANIFEST_NAME" "$BUILT_AT" <<'PY'
import json, sys
out, token, name, built = sys.argv[1:5]
doc = {
    "tokenId": token,
    "clientName": name,
    "packageId": "Ops.Runtime.Seed",
    "version": "1.0.0",
    "embeddedCredential": True,
    "builtAtUtc": built,
    "deliverables": [
        "Ops.Runtime.Seed.1.0.0.nupkg",
        "Ops.Runtime.Seed.dll",
        "INSTALL.txt"
    ],
}
with open(out, "w", encoding="utf-8") as f:
    json.dump(doc, f, indent=2)
    f.write("\n")
PY

cat > "$CLIENT_DIR/INSTALL.txt" <<EOF
Ops.Runtime.Seed — paczka dla klienta: $MANIFEST_NAME
Token (wbudowany w DLL): $TOKEN
Zbudowano: $BUILT_AT UTC

UiPath Studio:
  1. Manage Packages → Settings → Local → folder:
     $(realpath "$CLIENT_DIR")
  2. Install: Ops.Runtime.Seed

Proces (jeden Invoke Code, BEZ tokenu w workflow):

  using Ops.Runtime.Seed;
  FlowRuntime.Activate(
      out apiEndpoint,
      out connectionString,
      out agentPrompt,
      out licenseOwner,
      out licenseValidTo);

Zły/brak licencji → robot kończy job (Environment.Exit).

Przed produkcją: obfuskuj Ops.Runtime.Seed.dll (sample/confuser.example.crproj).
EOF

echo ""
echo "Client package ready:"
echo "  $CLIENT_DIR/"
echo "    $NUPKG_NAME"
echo "    Ops.Runtime.Seed.dll"
echo "    manifest.json"
echo "    INSTALL.txt"
echo ""
echo "Token $TOKEN is embedded (XOR). Do NOT commit dist/ to git."
