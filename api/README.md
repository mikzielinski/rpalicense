# Ops License API

Serwer licencyjny z autoryzacją **handshake** — panel (GitHub Pages) i roboty łączą się tylko z tym API.  
Dane operacyjne (katalog, audit, telemetria) są w **Neon PostgreSQL** — bez PAT GitHub na serwerze.

## Architektura

| Warstwa | Usługa |
|---------|--------|
| Panel WWW | GitHub Pages (`mikzielinski.github.io/rpalicense`) |
| API | Fly.io (`rpalicense.fly.dev`) — zamiast Render |
| Baza | Neon PostgreSQL (`DATABASE_URL`) |

## Uruchomienie lokalne

```bash
export DATABASE_URL='postgresql://...'
export OPS_OPERATOR_SECRET=twoj-sekret-operatora
export OPS_SESSION_SIGNING_KEY=$(openssl rand -hex 32)
export OPS_SEED_PEPPER=test-pepper-ops-runtime-seed-2026

dotnet run --project api/Ops.License.Api/Ops.License.Api.csproj
```

Bez `DATABASE_URL` API startuje z pamięcią RAM (tylko dev/test).

## Neon — migracja i seed (CLI)

```bash
export DATABASE_URL='postgresql://neondb_owner:...@ep-....neon.tech/neondb?sslmode=require'
./scripts/neon-bootstrap.sh
```

Skrypt tworzy tabele i importuje `docs/assets/seed.jwt`, audit i robot-events.

## Deploy na Fly.io (zamiast Render)

```bash
curl -L https://fly.io/install.sh | sh
flyctl auth login

export DATABASE_URL='postgresql://...'
export OPS_OPERATOR_SECRET='...'
export OPS_SESSION_SIGNING_KEY='...'
./scripts/fly-deploy.sh
```

Po deploy API: `https://rpalicense.fly.dev`

W panelu (GitHub Pages) ustaw **URL API** na ten adres i **Sekret operatora** (`OPS_OPERATOR_SECRET`).

## Docker (lokalnie / własny VPS)

```bash
docker build -f api/Dockerfile -t ops-license-api .
docker run -p 8080:8080 \
  -e DATABASE_URL='postgresql://...' \
  -e OPS_OPERATOR_SECRET=... \
  -e OPS_SESSION_SIGNING_KEY=... \
  -e OPS_SEED_PEPPER=... \
  ops-license-api
```

## Protokół handshake

### Robot (runtime)

1. `POST /v1/runtime/challenge` — `{ "machine": "ROBOT-01" }`
2. `POST /v1/runtime/authorize` — `{ tokenId, machine, challengeId, clientNonce, proof }`
3. Odpowiedź: `{ success, code, sessionToken, profile }`
4. Telemetria: `POST /v1/runtime/telemetry` z `Authorization: Bearer {sessionToken}`

### Panel (operator)

1. `POST /v1/operator/challenge` — `{ "operatorId": "mikolaj" }`
2. `POST /v1/operator/session` — `{ operatorId, challengeId, clientNonce, proof }`
3. Kolejne żądania: `Authorization: Bearer {sessionToken}`

## Endpointy operatora (sesja wymagana)

| Metoda | Ścieżka | Opis |
|--------|---------|------|
| GET | `/v1/catalog` | Pobierz `seed.jwt` |
| POST | `/v1/catalog/publish` | Opublikuj nowy JWT |
| GET/POST | `/v1/audit` | Dziennik operatora |
| GET | `/v1/robot-events` | Zdarzenia robotów |

## Robot (UiPath)

```
OPS_SEED_API_URL=https://rpalicense.fly.dev
OPS_SEED_PEPPER=...          # ten sam co na serwerze
OPS_SEED_TELEMETRY=1
```

## Panel — logowanie OAuth (GitHub / Google)

Działa **tylko dla kont już dodanych przez admina** z powiązanym GitHub login lub Google email.

### 1. OAuth Apps

**GitHub** → Settings → Developer settings → OAuth App:
- Homepage: `https://mikzielinski.github.io/rpalicense`
- Callback: `https://rpalicense.fly.dev/v1/panel/oauth/github/callback`

**Google** → Cloud Console → OAuth client (Web):
- Authorized redirect URI: `https://rpalicense.fly.dev/v1/panel/oauth/google/callback`

### 2. Sekrety na Fly

```bash
flyctl secrets set \
  OPS_OAUTH_GITHUB_CLIENT_ID='...' \
  OPS_OAUTH_GITHUB_CLIENT_SECRET='...' \
  OPS_OAUTH_GOOGLE_CLIENT_ID='...' \
  OPS_OAUTH_GOOGLE_CLIENT_SECRET='...' \
  OPS_PANEL_PUBLIC_URL='https://mikzielinski.github.io/rpalicense' \
  OPS_API_PUBLIC_URL='https://rpalicense.fly.dev' \
  --app rpalicense
```

### 3. Powiązanie konta (admin)

Przy tworzeniu konta podaj **GitHub login** (np. `mikzielinski`) lub **Google email**.  
Dla istniejącego konta: `PATCH /v1/panel/accounts/{username}` z `{ "githubLogin": "..." }`.

## Zmienne środowiskowe serwera

| Zmienna | Opis |
|---------|------|
| `DATABASE_URL` | Neon PostgreSQL (pooler, `sslmode=require`) |
| `OPS_PANEL_ADMIN_PASSWORD` | Hasło pierwszego admina (`mikolaj`) |
| `OPS_PANEL_ADMIN_GITHUB_LOGIN` | Opcjonalny GitHub login admina (OAuth) |
| `OPS_OAUTH_GITHUB_CLIENT_ID` / `SECRET` | OAuth GitHub panelu |
| `OPS_OAUTH_GOOGLE_CLIENT_ID` / `SECRET` | OAuth Google panelu |
| `OPS_PANEL_PUBLIC_URL` | URL panelu (GitHub Pages) |
| `OPS_API_PUBLIC_URL` | Publiczny URL API (Fly) |
| `OPS_SESSION_SIGNING_KEY` | Klucz podpisu sesji HMAC |
| `OPS_SEED_PEPPER` | Pepper runtime (musi zgadzać się z robotami) |
| `OPS_SEED_ENVELOPE_*` | Klucze koperty JWT katalogu |
