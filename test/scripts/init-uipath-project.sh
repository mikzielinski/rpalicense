#!/usr/bin/env bash
set -euo pipefail

# Run on MacBook Pro after `uip login`.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$ROOT/test/uipath/RuntimeGateTest"
PACKAGES="$ROOT/test/packages"

source "${HOME}/.bashrc" 2>/dev/null || true
export PATH="${HOME}/.npm-global/bin:${PATH}"

if ! command -v uip >/dev/null 2>&1; then
  echo "Install UiPath CLI: npm install -g @uipath/cli" >&2
  exit 1
fi

echo "Pack Ops.Runtime.Seed for local UiPath feed..."
mkdir -p "$PACKAGES"
(cd "$ROOT/src/Ops.Runtime.Seed" && dotnet pack -c Release -p:LangVersion=latest -o "$PACKAGES" >/dev/null)

if [[ ! -f "$PROJECT/project.json" ]]; then
  echo "Scaffolding UiPath project (requires signed-in uip session)..."
  mkdir -p "$(dirname "$PROJECT")"
  uip rpa create-project \
    --name RuntimeGateTest \
    --location "$(dirname "$PROJECT")" \
    --expression-language CSharp \
    --target-framework Portable \
    --description "Ops.Runtime.Seed license gate test robot"
fi

echo "Copy coded workflow sources..."
cp "$ROOT/test/uipath/RuntimeGateTest/Main.cs" "$PROJECT/Main.cs"
cp "$ROOT/test/uipath/RuntimeGateTest/RuntimeGateTests.cs" "$PROJECT/RuntimeGateTests.cs"
cp "$ROOT/test/uipath/RuntimeGateTest/project.json" "$PROJECT/project.json"
cp "$ROOT/test/uipath/RuntimeGateTest/NuGet.config" "$PROJECT/NuGet.config"

echo "Building UiPath project..."
(cd "$PROJECT" && uip rpa build --skip-analyze)

echo "Done. Project: $PROJECT"
