#!/usr/bin/env bash
# Buduje Ops.Runtime.Seed.nupkg i kopiuje do packages/ (commitowane w repo).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/artifacts/nuget"
PACKAGES="$ROOT/packages"
mkdir -p "$OUT" "$PACKAGES"

dotnet build "$ROOT/src/Ops.Runtime.Seed/Ops.Runtime.Seed.csproj" -c Release -v q
dotnet pack "$ROOT/src/Ops.Runtime.Seed/Ops.Runtime.Seed.csproj" -c Release --no-build -o "$OUT" -v q

NUPKG=$(ls -1 "$OUT"/*.nupkg | tail -1)
cp "$NUPKG" "$PACKAGES/"
BASENAME=$(basename "$NUPKG")

echo ""
echo "NuGet package:"
echo "  $PACKAGES/$BASENAME   ← gotowy do commit / UiPath"
echo "  $NUPKG                ← kopia build"
echo ""
echo "UiPath Studio → Manage Packages → Settings → Local folder:"
echo "  $PACKAGES"
