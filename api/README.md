# Ops License API

Serwer pośredniczący między panelem / robotami a GitHub Contents API.

**PAT GitHub jest tylko tutaj (na serwerze).** Panel i roboty używają prostego klucza API.

## Uruchomienie lokalne

```bash
export GITHUB_TOKEN=ghp_...
export OPS_API_OPERATOR_KEY=twoj-klucz-operatora
export OPS_API_ROBOT_KEY=twoj-klucz-robota

dotnet run --project api/Ops.License.Api/Ops.License.Api.csproj
```

Domyślnie: `http://localhost:5000` (lub port z launchSettings).

## Docker

```bash
docker build -f api/Dockerfile -t ops-license-api .
docker run -p 8080:8080 \
  -e GITHUB_TOKEN=ghp_... \
  -e OPS_API_OPERATOR_KEY=... \
  -e OPS_API_ROBOT_KEY=... \
  ops-license-api
```

## Endpointy

| Metoda | Ścieżka | Klucz | Opis |
|--------|---------|-------|------|
| GET | `/health` | — | Health check |
| GET | `/v1/seed` | operator | Pobierz aktualny `seed.jwt` + SHA |
| POST | `/v1/seed/publish` | operator | Opublikuj nowy JWT |
| POST | `/v1/audit` | operator | Nadpisz `audit-log.json` |
| POST | `/v1/telemetry` | robot / operator | Dopisz zdarzenie sprawdzenia licencji |

Nagłówek autoryzacji: `X-Api-Key: ...` lub `Authorization: Bearer ...`

## Panel (GitHub Pages)

W ustawieniach panelu:
- **URL API** — np. `https://twoja-domena.pl`
- **Klucz API (operator)** — wartość `OPS_API_OPERATOR_KEY`

Publikacja `seed.jwt` i dziennika operatora idzie przez API (bez PAT w przeglądarce).

## Robot (UiPath)

Zmienne środowiskowe:

```
OPS_SEED_TELEMETRY=1
OPS_SEED_TELEMETRY_API_URL=https://twoja-domena.pl
OPS_SEED_TELEMETRY_API_KEY=klucz-robota
```

## Zmienne środowiskowe serwera

| Zmienna | Opis |
|---------|------|
| `GITHUB_TOKEN` | PAT z uprawnieniem zapisu do repo |
| `OPS_API_OPERATOR_KEY` | Klucz panelu |
| `OPS_API_ROBOT_KEY` | Klucz robotów (telemetria) |
| `GitHub__Owner` | owner repo (domyślnie mikzielinski) |
| `GitHub__Repo` | nazwa repo |
| `GitHub__Branch` | branch (main) |
