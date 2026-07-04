# Ops.Runtime.Seed

Neutralna biblioteka bootstrapu runtime dla **UiPath**:
- pobiera zaszyfrowany katalog opakowany w JWT (JWS + encrypted payload),
- weryfikuje podpis wpisu (`seal`, RSA),
- odszyfrowuje payload klienta (AES-GCM, klucz zależny od `tokenId + pepper`),
- ma cache offline z grace period,
- udostępnia status walidacji (`Bootstrapper.LastCheck`) do logowania w bocie,
- wspiera **zdalne odcięcie/odnowienie** licencji bez restartu robota.

**Manual implementacji UiPath:** [docs/uipath-implementation.md](docs/uipath-implementation.md)

**Panel operatora (GitHub Pages):** [https://mikzielinski.github.io/rpalicense/](https://mikzielinski.github.io/rpalicense/)

---

## Struktura repozytorium

| Ścieżka | Opis |
|---------|------|
| `src/Ops.Runtime.Seed` | Biblioteka .NET 6 → paczka `.nupkg` |
| `keygen` | CLI: klucze RSA, wpisy katalogu, `seed.jwt`, JWK |
| `docs` | Panel operatora (GitHub Pages) + dokumentacja |
| `sample/Ops.Runtime.Seed.TestApp` | Przykładowy bot (Invoke Code) |
| `scripts` | Testy E2E, publikacja licencji przez GitHub API |
| `tests` | xUnit (23 testy) + DependencyHost (ModuleInit) |

---

## Szybki start (UiPath)

1. Zbuduj paczkę: `dotnet pack src/Ops.Runtime.Seed -c Release`
2. Zainstaluj `Ops.Runtime.Seed` w projekcie Studio (feed NuGet).
3. Utwórz Asset Orchestrator z tokenem `RT-...`.
4. Na początku procesu (Invoke Code):

```csharp
using Ops.Runtime.Seed;

if (!Bootstrapper.TryInitialize(runtimeToken, out var profile))
    throw new System.Exception($"Runtime gate: {Bootstrapper.LastCheck.Code}");

// użyj profile.ApiEndpoint, profile.ConnectionString, ...
```

Szczegóły: **[docs/uipath-implementation.md](docs/uipath-implementation.md)**

---

## Build biblioteki

```bash
cd src/Ops.Runtime.Seed
dotnet build -c Release
dotnet pack -c Release
```

Wynik: `bin/Release/Ops.Runtime.Seed.1.0.0.nupkg`

---

## Keygen — klucze i katalog

### Klucze RSA

```bash
cd keygen
dotnet run -- newkeys ./keys
```

- `keys/seal.private.pem` — tylko u operatora (keygen / panel)
- `keys/seal.public.pem` — osadzany w bibliotece (`PublicSealKeyPem`)

### Wpis katalogu

Przykładowy payload klienta: `sample/payload.example.json`

```bash
dotnet run -- issue ./keys/seal.private.pem "PEPPER" "RT-2026-CLIENT-001" "Klient Sp. z o.o." "2026-12-31T23:59:59Z" "ROBOT01,ROBOT02" ../sample/payload.example.json
```

Wynik wklej do `catalog.json` → `{ "entries": [ ... ] }`.

### Opakowanie do seed.jwt

```bash
dotnet run -- wrapjwt ./catalog.json "JWT-SIGNING-KEY" "ENVELOPE-PEPPER" "https://twojadomena.pl" "ops-runtime-seed" "2026-12-31T23:59:59Z" > seed.jwt
```

Opublikuj jako `docs/assets/seed.jwt` (GitHub Pages).

---

## Zdalne odcięcie / odnowienie licencji

| Akcja | Efekt na bocie |
|-------|----------------|
| `enabled=false` | `boot-0x12` przy kolejnej walidacji |
| `enabled=true` | normalna praca (`boot-ok-remote`) |

### Panel WWW

1. Włącz GitHub Pages z folderu `/docs`.
2. Otwórz panel → **Odetnij** / **Odnow** → **Przelicz seal** → **Generuj seed.jwt** → **Opublikuj**.

### CLI (GitHub Contents API)

```bash
./scripts/license-api.sh deactivate   # odcięcie
./scripts/license-api.sh activate     # odnowienie
./scripts/license-api.sh status       # stan repo + Pages
```

Wymaga: [GitHub CLI](https://cli.github.com/) (`gh auth login`).

Skróty:

```bash
./scripts/revoke-license-on-pages.sh    # → deactivate
./scripts/restore-license-on-pages.sh     # → activate
```

---

## Testowanie

```bash
# Fixture + build + 23 testy xUnit
./scripts/run-test-report.sh

# Cykl licencji: TestApp + API activate/deactivate + Pages
./scripts/test-license-lifecycle.sh

# Tylko testy
dotnet test tests/Ops.Runtime.Seed.Tests -c Release
```

Przykładowy bot:

```bash
dotnet build sample/Ops.Runtime.Seed.TestApp -c Release
FLOW_RUNTIME_TOKEN=RT-TEST-REPORT-001 dotnet sample/Ops.Runtime.Seed.TestApp/bin/Release/net6.0/Ops.Runtime.Seed.TestApp.dll
```

---

## Konfiguracja biblioteki

Stałe produkcyjne są w **`BootstrapperSettings.cs`** (kompilowane do DLL):

- `SourceUrl` — URL do `seed.jwt` (GitHub Pages)
- `Pepper`, `EnvelopePepper`, `EnvelopeSigningKey`
- `EnvelopeIssuer`, `EnvelopeAudience`
- `PublicSealKeyPem` — klucz publiczny RSA

Override tylko na potrzeby testów — zmienne `OPS_SEED_*` (patrz manual UiPath).

---

## Auto-inicjalizacja (ModuleInit)

Gdy robot ma ustawione `FLOW_RUNTIME_TOKEN` / `APP_BOOT_TOKEN` w środowisku, biblioteka **w tle** próbuje init po załadowaniu assembly.

**Zalecenie:** mimo ModuleInit zawsze wołaj `TryInitialize` na początku procesu — pełna kontrola błędów i brak wyścigu z init w tle.

---

## Ważne

- Panel przetwarza sekrety lokalnie w przeglądarce; PAT do publikacji traktuj jak credential administracyjny.
- Obfuskację (ConfuserEx) rób na finalnej DLL i testuj w UiPath po obfuskacji (`sample/confuser.example.crproj`).
- Po publikacji `seed.jwt` GitHub Pages CDN może potrzebować 1–4 min na odświeżenie.

---

## Dokumentacja

- **[Manual implementacji UiPath](docs/uipath-implementation.md)** — wdrożenie krok po kroku
- **[Panel operatora](docs/index.html)** — zarządzanie licencjami
- **`sample/Ops.Runtime.Seed.TestApp`** — referencyjny kod bota
