# GitHub Actions + Pages (bez osobnego serwera)

Panel na **GitHub Pages** wysyła zdarzenia do **GitHub Actions**. Workflow commituje pliki do `docs/assets/` — Pages odświeża się automatycznie.

## 1. Sekrety w repo

W **Settings → Secrets and variables → Actions** dodaj:

| Secret | Opis |
|--------|------|
| `OPS_API_OPERATOR_KEY` | Klucz panelu (dowolny długi string) |
| `OPS_API_ROBOT_KEY` | Klucz robotów (telemetria) |

## 2. Panel (GitHub Pages)

W ustawieniach panelu:

| Pole | Wartość |
|------|---------|
| URL API | **zostaw puste** |
| Klucz operatora | ten sam co `OPS_API_OPERATOR_KEY` |
| GitHub token | PAT z uprawnieniem `repo` (tylko do `repository_dispatch`) |
| GitHub owner / repo | np. `mikzielinski` / `rpalicense` |

Kliknij **Testuj połączenie** — powinien przejść workflow `ping` w zakładce Actions.

## 3. Robot (telemetria)

```
OPS_SEED_TELEMETRY=1
OPS_SEED_TELEMETRY_DISPATCH=1
OPS_SEED_TELEMETRY_API_KEY=<OPS_API_ROBOT_KEY>
OPS_SEED_DISPATCH_TOKEN=ghp_...   (PAT z repo — tylko dispatch)
```

Opcjonalnie: `OPS_SEED_DISPATCH_OWNER`, `OPS_SEED_DISPATCH_REPO`.

## 4. Co robi workflow

Plik: `.github/workflows/license-ops.yml`

| Zdarzenie | Efekt |
|-----------|--------|
| `publish-seed` | Zapis `docs/assets/seed.jwt` |
| `publish-audit` | Zapis `docs/assets/audit-log.json` |
| `robot-telemetry` | Dopisanie do `docs/assets/robot-events.json` |
| `ping` | Test klucza (bez commita) |

## 5. Własny serwer API (opcjonalnie)

Jeśli nie chcesz używać Actions, uruchom `api/Ops.License.Api` i podaj **URL API** w panelu — patrz `api/README.md`.
