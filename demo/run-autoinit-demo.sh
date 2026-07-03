#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/demo/pages-demo.env"
exec dotnet run --project "$ROOT/demo/Ops.Runtime.Seed.AutoInitDemo" "$@"
