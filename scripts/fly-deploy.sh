#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export PATH="${HOME}/.fly/bin:${PATH}"

if ! command -v flyctl >/dev/null 2>&1; then
  echo "Install flyctl: curl -L https://fly.io/install.sh | sh" >&2
  exit 1
fi

if [[ -z "${DATABASE_URL:-}" ]]; then
  echo "Set DATABASE_URL before deploy." >&2
  exit 1
fi

if [[ -z "${OPS_OPERATOR_SECRET:-}" || -z "${OPS_SESSION_SIGNING_KEY:-}" ]]; then
  echo "Set OPS_OPERATOR_SECRET and OPS_SESSION_SIGNING_KEY before deploy." >&2
  exit 1
fi

flyctl apps list >/dev/null 2>&1 || {
  echo "Run: flyctl auth login" >&2
  exit 1
}

if ! flyctl apps list 2>/dev/null | grep -q '^rpalicense'; then
  flyctl launch --config fly.toml --copy-config --no-deploy --name rpalicense --region ams --yes
fi

flyctl secrets set \
  DATABASE_URL="$DATABASE_URL" \
  OPS_OPERATOR_SECRET="$OPS_OPERATOR_SECRET" \
  OPS_SESSION_SIGNING_KEY="$OPS_SESSION_SIGNING_KEY" \
  OPS_SEED_PEPPER="${OPS_SEED_PEPPER:-test-pepper-ops-runtime-seed-2026}" \
  OPS_SEED_ENVELOPE_PEPPER="${OPS_SEED_ENVELOPE_PEPPER:-test-envelope-pepper-ops-runtime-2026}" \
  OPS_SEED_ENVELOPE_SIGNING_KEY="${OPS_SEED_ENVELOPE_SIGNING_KEY:-test-jwt-signing-key-ops-runtime-seed-2026}" \
  OPS_SEED_ENVELOPE_ISSUER="${OPS_SEED_ENVELOPE_ISSUER:-https://mikzielinski.github.io/rpalicense}" \
  OPS_SEED_ENVELOPE_AUDIENCE="${OPS_SEED_ENVELOPE_AUDIENCE:-ops-runtime-seed}" \
  ${OPS_SEED_PUBLIC_SEAL_KEY_PEM:+OPS_SEED_PUBLIC_SEAL_KEY_PEM="$OPS_SEED_PUBLIC_SEAL_KEY_PEM"}

flyctl deploy --config fly.toml

echo "API URL: https://rpalicense.fly.dev"
