#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FIXTURES="$ROOT/test-fixtures"
KEYS="$FIXTURES/keys"
CONFIG="$FIXTURES/test-config.json"
KEYGEN="dotnet run --project $ROOT/keygen/SeedForge.csproj -c Release --no-build --"

echo "==> [0/5] Build keygen"
dotnet build "$ROOT/keygen/SeedForge.csproj" -c Release --nologo -v q

mkdir -p "$KEYS" "$FIXTURES/catalog"

read_config() { jq -r ".$1" "$CONFIG"; }

SOURCE_URL=$(read_config sourceUrl)
PEPPER=$(read_config pepper)
TOKEN_ID=$(read_config tokenId)
OWNER=$(read_config owner)
VALID_TO=$(read_config validToUtc)
HOSTS=$(jq -r '.hosts | join(",")' "$CONFIG")
JWT_KEY=$(read_config envelopeSigningKey)
ENV_PEPPER=$(read_config envelopePepper)
ISSUER=$(read_config envelopeIssuer)
AUDIENCE=$(read_config envelopeAudience)

echo "==> [1/5] RSA seal keys"
if [[ -f "$KEYS/seal.private.pem" && -f "$KEYS/seal.public.pem" ]]; then
  echo "    reusing existing keys in $KEYS"
else
  $KEYGEN newkeys "$KEYS"
fi

echo "==> [2/4] Issuing registered license: $TOKEN_ID"
ENTRY=$($KEYGEN issue "$KEYS/seal.private.pem" "$PEPPER" "$TOKEN_ID" "$OWNER" "$VALID_TO" "$HOSTS" "$ROOT/sample/payload.example.json")
echo "$ENTRY" | jq -s '{entries: .}' > "$FIXTURES/catalog/live.json"

echo "==> [3/4] Wrapping live seed.jwt"
$KEYGEN wrapjwt "$FIXTURES/catalog/live.json" "$JWT_KEY" "$ENV_PEPPER" "$ISSUER" "$AUDIENCE" "$VALID_TO" \
  > "$FIXTURES/seed.live.jwt"

echo "==> [4/4] Building disabled catalog + seed.jwt"
jq '.entries[0].enabled = false' "$FIXTURES/catalog/live.json" > "$FIXTURES/catalog/disabled-raw.json"
$KEYGEN reseal "$FIXTURES/catalog/disabled-raw.json" "$KEYS/seal.private.pem" > "$FIXTURES/catalog/disabled.json"
$KEYGEN wrapjwt "$FIXTURES/catalog/disabled.json" "$JWT_KEY" "$ENV_PEPPER" "$ISSUER" "$AUDIENCE" "$VALID_TO" \
  > "$FIXTURES/seed.disabled.jwt"

echo "==> [5/5] Building host-restricted catalog + seed.jwt"
jq '.entries[0].hosts = ["ROBOT01"]' "$FIXTURES/catalog/live.json" > "$FIXTURES/catalog/host-restricted-raw.json"
$KEYGEN reseal "$FIXTURES/catalog/host-restricted-raw.json" "$KEYS/seal.private.pem" > "$FIXTURES/catalog/host-restricted.json"
$KEYGEN wrapjwt "$FIXTURES/catalog/host-restricted.json" "$JWT_KEY" "$ENV_PEPPER" "$ISSUER" "$AUDIENCE" "$VALID_TO" \
  > "$FIXTURES/seed.host-restricted.jwt"

cp "$KEYS/seal.public.pem" "$FIXTURES/seal.public.pem"

jq -n \
  --arg sourceUrl "$SOURCE_URL" \
  --arg tokenId "$TOKEN_ID" \
  --arg pepper "$PEPPER" \
  --arg envPepper "$ENV_PEPPER" \
  --arg jwtKey "$JWT_KEY" \
  --arg issuer "$ISSUER" \
  --arg audience "$AUDIENCE" \
  --arg validTo "$VALID_TO" \
  --arg publicPem "$(cat "$KEYS/seal.public.pem")" \
  --arg liveJwt "$(cat "$FIXTURES/seed.live.jwt")" \
  --arg disabledJwt "$(cat "$FIXTURES/seed.disabled.jwt")" \
  --arg hostRestrictedJwt "$(cat "$FIXTURES/seed.host-restricted.jwt")" \
  --arg catalog "$(cat "$FIXTURES/catalog/live.json")" \
  '{
    sourceUrl: $sourceUrl,
    tokenId: $tokenId,
    pepper: $pepper,
    envelopePepper: $envPepper,
    envelopeSigningKey: $jwtKey,
    envelopeIssuer: $issuer,
    envelopeAudience: $audience,
    validToUtc: $validTo,
    publicSealKeyPem: $publicPem,
    liveJwt: $liveJwt,
    disabledJwt: $disabledJwt,
    hostRestrictedJwt: $hostRestrictedJwt,
    catalog: ($catalog | fromjson)
  }' > "$FIXTURES/manifest.json"

PAGES_ASSET="$ROOT/docs/assets/seed.jwt"
mkdir -p "$(dirname "$PAGES_ASSET")"
cp "$FIXTURES/seed.live.jwt" "$PAGES_ASSET"
echo "==> [6/6] Published seed.jwt for GitHub Pages -> docs/assets/seed.jwt"

echo ""
echo "Fixtures generated:"
echo "  tokenId:       $TOKEN_ID"
echo "  sourceUrl:     $SOURCE_URL"
echo "  live catalog:  $FIXTURES/catalog/live.json"
echo "  live JWT:      $FIXTURES/seed.live.jwt"
echo "  pages JWT:     $PAGES_ASSET"
echo "  disabled JWT:  $FIXTURES/seed.disabled.jwt"
echo "  manifest:      $FIXTURES/manifest.json"
