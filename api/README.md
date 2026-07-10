# Ops License API

Serwer licencyjny z autoryzacją **handshake** — panel i roboty łączą się tylko z tym API.  
Token GitHub (`GITHUB_TOKEN`) jest **wyłącznie na serwerze**; klient nie widzi owner/repo ani PAT.

## Uruchomienie lokalne

```bash
export GITHUB_TOKEN=ghp_...
export OPS_OPERATOR_SECRET=twoj-sekret-operatora
export OPS_SESSION_SIGNING_KEY=$(openssl rand -hex 32)
export OPS_SEED_PEPPER=test-pepper-ops-runtime-seed-2026

dotnet run --project api/Ops.License.Api/Ops.License.Api.csproj
```

## Docker

```bash
docker build -f api/Dockerfile -t ops-license-api .
docker run -p 8080:8080 \
  -e GITHUB_TOKEN=ghp_... \
  -e OPS_OPERATOR_SECRET=... \
  -e OPS_SESSION_SIGNING_KEY=... \
  -e OPS_SEED_PEPPER=... \
  ops-license-api
```

## Protokół handshake

### Robot (runtime)

1. `POST /v1/runtime/challenge` — `{ "machine": "ROBOT-01" }`
2. `POST /v1/runtime/authorize` — `{ tokenId, machine, challengeId, clientNonce, proof }`  
   `proof = HMAC-SHA256(SHA256(token:pepper), challengeId|serverNonce|clientNonce|machine|tokenId)`
3. Odpowiedź: `{ success, code, sessionToken, profile }`
4. Telemetria: `POST /v1/runtime/telemetry` z `Authorization: Bearer {sessionToken}`

### Panel (operator)

1. `POST /v1/operator/challenge` — `{ "operatorId": "mikolaj" }`
2. `POST /v1/operator/session` — `{ operatorId, challengeId, clientNonce, proof }`  
   `proof = HMAC-SHA256(SHA256(operatorSecret), challengeId|serverNonce|clientNonce|operatorId|operatorId)`
3. Kolejne żądania: `Authorization: Bearer {sessionToken}`

## Endpointy operatora (sesja wymagana)

| Metoda | Ścieżka | Opis |
|--------|---------|------|
| GET | `/v1/catalog` | Pobierz `seed.jwt` |
| POST | `/v1/catalog/publish` | Opublikuj nowy JWT |
| GET/POST | `/v1/audit` | Dziennik operatora |
| GET | `/v1/robot-events` | Zdarzenia robotów |

## Panel (GitHub Pages)

W ustawieniach:
- **URL API** — np. `https://license.example.com`
- **Sekret operatora** — ta sama wartość co `OPS_OPERATOR_SECRET` na serwerze

Bez tokenów GitHub w przeglądarce.

## Robot (UiPath)

```
OPS_SEED_API_URL=https://license.example.com
OPS_SEED_PEPPER=...          # ten sam co na serwerze
OPS_SEED_TELEMETRY=1
```

Biblioteka sama wykonuje handshake i używa sesji do telemetrii.

## Zmienne środowiskowe serwera

| Zmienna | Opis |
|---------|------|
| `GITHUB_TOKEN` | PAT z zapisem do repo (tylko serwer) |
| `OPS_OPERATOR_SECRET` | Sekret operatora (panel) |
| `OPS_SESSION_SIGNING_KEY` | Klucz podpisu sesji HMAC |
| `OPS_SEED_PEPPER` | Pepper runtime (musi zgadzać się z robotami) |
| `OPS_SEED_ENVELOPE_*` | Klucze koperty JWT katalogu |
| `GitHub__Owner`, `GitHub__Repo`, `GitHub__Branch` | Repo docelowe (domyślnie mikzielinski/rpalicense/main) |
