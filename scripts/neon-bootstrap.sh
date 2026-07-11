#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ -z "${DATABASE_URL:-}" && -z "${NEON_DATABASE_URL:-}" ]]; then
  echo "Set DATABASE_URL (Neon connection string)." >&2
  exit 1
fi

dotnet run --project scripts/neon-bootstrap/neon-bootstrap.csproj
