#!/usr/bin/env bash
# Buduje Ops.Runtime.Seed.nupkg do artifacts/nuget/
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/artifacts/nuget"
mkdir -p "$OUT"

dotnet build "$ROOT/src/Ops.Runtime.Seed/Ops.Runtime.Seed.csproj" -c Release -v q
dotnet pack "$ROOT/src/Ops.Runtime.Seed/Ops.Runtime.Seed.csproj" -c Release --no-build -o "$OUT" -v q

NUPKG=$(ls -1 "$OUT"/*.nupkg | tail -1)
echo ""
echo "NuGet package:"
echo "  $NUPKG"
echo ""
echo "UiPath Studio → Manage Packages → Settings → add folder:"
echo "  $OUT"
