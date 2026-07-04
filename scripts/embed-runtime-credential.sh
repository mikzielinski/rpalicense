#!/usr/bin/env bash
# XOR-encode runtime token into RuntimeCredential.Payload.g.cs (per-client NuGet).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/src/Ops.Runtime.Seed/RuntimeCredential.Payload.g.cs"
TOKEN="${1:-}"

if [ -z "$TOKEN" ]; then
  cat > "$OUT" <<'CS'
namespace Ops.Runtime.Seed;

internal static partial class RuntimeCredential
{
    private static readonly byte[] Payload = Array.Empty<byte>();
}
CS
  echo "Runtime credential cleared (empty payload)."
  exit 0
fi

BYTES=$(python3 - "$TOKEN" <<'PY'
import sys
token = sys.argv[1]
mask = b"OpsRuntimeSeed2026"
payload = [ord(c) ^ mask[i % len(mask)] for i, c in enumerate(token)]
print(", ".join(str(b) for b in payload))
PY
)

cat > "$OUT" <<CS
namespace Ops.Runtime.Seed;

internal static partial class RuntimeCredential
{
    private static readonly byte[] Payload = new byte[] { $BYTES };
}
CS

echo "Embedded runtime credential (${#TOKEN} chars) → $OUT"
