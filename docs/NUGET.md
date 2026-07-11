# UiPath.System.RoboticSecurity — NuGet

Pakiet biblioteki runtime dla robotów UiPath. Budowany i publikowany automatycznie przez GitHub Actions.

## Automatyczny build

Workflow: `.github/workflows/nuget-pack.yml`

| Trigger | Co robi |
|---------|---------|
| Push na `main` (zmiana w `src/Ops.Runtime.Seed/`) | test → pack → publish |
| Ręcznie (Actions → NuGet Pack → Run workflow) | to samo |

Artefakty:
- **GitHub Packages** — `https://github.com/mikzielinski/rpalicense/packages`
- **Repo** — `release/windows-uipath/nuget/*.nupkg` + `lib/*.dll` (dla `INSTALUJ.cmd`)
- **Actions artifact** — pobranie z ostatniego runu workflow

## Instalacja w UiPath (lokalny feed — najprościej)

1. Pobierz folder `release/windows-uipath` z repo (lub uruchom `INSTALUJ.cmd` na Windows)
2. UiPath Studio → **Manage Sources** → Add:
   - Name: `OpsRuntime Local`
   - Source: `%USERPROFILE%\OpsRuntime\nuget`
3. **Manage Packages** → zainstaluj `UiPath.System.RoboticSecurity`

## Instalacja z GitHub Packages

Wymaga PAT z uprawnieniem `read:packages`.

```bash
dotnet nuget add source "https://nuget.pkg.github.com/mikzielinski/index.json" \
  --name github-ops \
  --username mikzielinski \
  --password YOUR_GITHUB_PAT \
  --store-password-in-clear-text

dotnet add package UiPath.System.RoboticSecurity --version 1.0.7
```

W UiPath: feed URL jak wyżej, login = GitHub user, password = PAT.

## Konfiguracja robota (Fly.io API)

Zmienne środowiskowe na maszynie robota (Panel Windows):

```
OPS_SEED_API_URL=https://rpalicense.fly.dev
OPS_SEED_PEPPER=test-pepper-ops-runtime-seed-2026
OPS_SEED_TELEMETRY=1
OPS_SEED_KILL_ON_DENY=1
OPS_SEED_GRACE_DAYS=7
```

- **Licencja** — handshake z API (bez seed.jwt na robocie)
- **Telemetria** — automatycznie na ten sam URL API (sesja z autoryzacji)
- **Offline fallback** — cache lokalny działa bez internetu przez `OPS_SEED_GRACE_DAYS` dni od ostatniego potwierdzenia online (domyślnie 7). Online możesz odciąć licencję zdalnie w panelu.

## Wersjonowanie

Wersja w `src/Ops.Runtime.Seed/Ops.Runtime.Seed.csproj` (`<Version>`). Po bumpie i pushu na `main` CI publikuje nową wersję.

## Build lokalny

```bash
dotnet pack src/Ops.Runtime.Seed/Ops.Runtime.Seed.csproj -c Release -o artifacts/nupkg
```

Windows (pełny bundle UiPath):

```powershell
.\scripts\build-windows.ps1
```
