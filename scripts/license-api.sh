#!/usr/bin/env bash
# Publikacja seed.jwt przez GitHub Contents API (jak panel docs/app.js).
# Użycie:
#   ./scripts/license-api.sh activate   # enabled=true  → nadanie licencji
#   ./scripts/license-api.sh deactivate # enabled=false → odcięcie licencji
#   ./scripts/license-api.sh status     # sprawdź enabled w repo i na Pages
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FIXTURES="$ROOT/test-fixtures"

GH_OWNER="${GH_OWNER:-mikzielinski}"
GH_REPO="${GH_REPO:-rpalicense}"
GH_BRANCH="${GH_BRANCH:-main}"
GH_PATH="${GH_PATH:-docs/assets/seed.jwt}"
KEYGEN="dotnet run --project $ROOT/keygen/SeedForge.csproj -c Release --no-build --"

usage() {
  echo "usage: license-api.sh {activate|deactivate|status}"
  exit 1
}

ensure_fixtures() {
  if [[ ! -f "$FIXTURES/seed.live.jwt" || ! -f "$FIXTURES/seed.disabled.jwt" ]]; then
    "$ROOT/scripts/generate-test-fixtures.sh" >/dev/null
  fi
}

jwt_for_action() {
  case "$1" in
    activate)   echo "$FIXTURES/seed.live.jwt" ;;
    deactivate) echo "$FIXTURES/seed.disabled.jwt" ;;
    *) usage ;;
  esac
}

get_remote_sha() {
  gh api "repos/${GH_OWNER}/${GH_REPO}/contents/${GH_PATH}?ref=${GH_BRANCH}" \
    --jq .sha 2>/dev/null || true
}

publish_jwt() {
  local action="$1"
  local jwt_file message
  jwt_file=$(jwt_for_action "$action")
  message=$([ "$action" = activate ] && echo "API: activate license (enabled=true)" || echo "API: deactivate license (enabled=false)")

  local content_b64 sha args=()
  content_b64=$(base64 -w 0 "$jwt_file")

  sha=$(get_remote_sha)
  if [[ -n "$sha" && "$sha" != "null" ]]; then
    args+=(-f "sha=$sha")
  fi

  echo "==> GitHub API PUT $GH_PATH ($action)"
  gh api -X PUT "repos/${GH_OWNER}/${GH_REPO}/contents/${GH_PATH}" \
    -f message="$message" \
    -f content="$content_b64" \
    -f branch="$GH_BRANCH" \
    "${args[@]}" \
    --jq '{commit: .commit.sha, content: .content.path}'

  cp "$jwt_file" "$ROOT/docs/assets/seed.jwt"
}

unwrap_enabled() {
  local file="$1"
  $KEYGEN unwrapjwt "$file" \
    "test-jwt-signing-key-ops-runtime-seed-2026" \
    "test-envelope-pepper-ops-runtime-2026" \
    "https://mikzielinski.github.io/rpalicense" \
    "ops-runtime-seed" 2>/dev/null | jq -r '.entries[0].enabled'
}

status() {
  local tmp pages_tmp
  tmp=$(mktemp)
  pages_tmp=$(mktemp)

  echo "Repo: $GH_OWNER/$GH_REPO @ $GH_BRANCH → $GH_PATH"
  if gh api "repos/${GH_OWNER}/${GH_REPO}/contents/${GH_PATH}?ref=${GH_BRANCH}" --jq .content 2>/dev/null | tr -d '\n' | base64 -d > "$tmp" 2>/dev/null; then
    echo -n "  repo catalog enabled: "
    unwrap_enabled "$tmp"
  else
    echo "  repo: brak pliku"
  fi

  if curl -sf "https://${GH_OWNER}.github.io/${GH_REPO}/assets/seed.jwt" -o "$pages_tmp" 2>/dev/null; then
    echo -n "  pages catalog enabled: "
    unwrap_enabled "$pages_tmp"
  else
    echo "  pages: niedostępne (404 lub cache)"
  fi

  rm -f "$tmp" "$pages_tmp"
}

wait_pages_enabled() {
  local expected="$1"
  local url="https://${GH_OWNER}.github.io/${GH_REPO}/assets/seed.jwt"
  local tmp
  tmp=$(mktemp)
  for i in $(seq 1 24); do
    if curl -sf "$url" -o "$tmp" 2>/dev/null; then
      if [[ "$(unwrap_enabled "$tmp")" == "$expected" ]]; then
        echo "    Pages enabled=$expected (attempt $i)"
        rm -f "$tmp"
        return 0
      fi
    fi
    echo "    attempt $i: czekam na Pages enabled=$expected..."
    sleep 10
  done
  rm -f "$tmp"
  echo "ERROR: Pages nie odswiezyl JWT (enabled=$expected)"
  return 1
}

ACTION="${1:-}"
[[ -z "$ACTION" ]] && usage

ensure_fixtures
dotnet build "$ROOT/keygen/SeedForge.csproj" -c Release -q >/dev/null 2>&1 || dotnet build "$ROOT/keygen/SeedForge.csproj" -c Release -q

case "$ACTION" in
  activate|deactivate)
    publish_jwt "$ACTION"
    if [[ "${SKIP_PAGES_WAIT:-}" != "1" ]]; then
      wait_pages_enabled $([ "$ACTION" = activate ] && echo true || echo false)
    fi
    status
    ;;
  status)
    status
    ;;
  *)
    usage
    ;;
esac
