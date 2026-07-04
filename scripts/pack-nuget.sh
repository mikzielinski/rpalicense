#!/usr/bin/env bash
# Buduje Ops.Runtime.Seed.nupkg i kopiuje do packages/ (commitowane w repo).
# Opcjonalnie: RUNTIME_TOKEN=RT-... lub --runtime-token RT-... (wbija token w DLL, bez env w UiPath).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/artifacts/nuget"
PACKAGES="$ROOT/packages"
RUNTIME_TOKEN="${RUNTIME_TOKEN:-}"

while [ $# -gt 0 ]; do
  case "$1" in
    --runtime-token)
      RUNTIME_TOKEN="${2:-}"
      shift 2
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

mkdir -p "$OUT" "$PACKAGES"

"$ROOT/scripts/embed-runtime-credential.sh" "$RUNTIME_TOKEN"

dotnet build "$ROOT/src/Ops.Runtime.Seed/Ops.Runtime.Seed.csproj" -c Release -v q
dotnet pack "$ROOT/src/Ops.Runtime.Seed/Ops.Runtime.Seed.csproj" -c Release --no-build -o "$OUT" -v q

NUPKG=$(ls -1 "$OUT"/*.nupkg | tail -1)
cp "$NUPKG" "$PACKAGES/"
BASENAME=$(basename "$NUPKG")

echo ""
echo "NuGet package:"
echo "  $PACKAGES/$BASENAME   ← gotowy do commit / UiPath"
echo "  $NUPKG                ← kopia build"
if [ -n "$RUNTIME_TOKEN" ]; then
  echo ""
  echo "Runtime token: embedded in DLL (klient nie ustawia tokenu w procesie)."
else
  echo ""
  echo "Runtime token: brak w DLL — ustaw FLOW_RUNTIME_TOKEN na robocie (zmienna maszynowa)."
fi
echo ""
echo "UiPath Studio → Manage Packages → Settings → Local folder:"
echo "  $PACKAGES"
