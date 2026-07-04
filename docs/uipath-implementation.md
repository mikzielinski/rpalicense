# Ops.Runtime.Seed — manual implementacji UiPath

Ten dokument opisuje krok po kroku, jak podłączyć bibliotekę `Ops.Runtime.Seed` do bota UiPath (Studio + Robot + Orchestrator), skonfigurować licencję runtime i obsłużyć zdalne odcięcie bez restartu maszyny.

## Spis treści

1. [Jak to działa](#1-jak-to-działa)
2. [Wymagania](#2-wymagania)
3. [Instalacja paczki](#3-instalacja-paczki)
4. [Konfiguracja Orchestratora](#4-konfiguracja-orchestratora)
5. [Implementacja w procesie](#5-implementacja-w-procesie-klient)
6. [Ukryte zabezpieczenie w NuGet](#6-ukryte-zabezpieczenie-w-nuget)
7. [Pola zwracane przez Activate](#7-pola-zwracane-przez-activate-out)
8. [Diagnostyka (operator)](#8-diagnostyka-tylko-operator--maintainer)
9. [Re-walidacja i odcięcie](#9-re-walidacja-i-odcięcie)
10. [Odcięcie i odnowienie licencji](#10-odcięcie-i-odnowienie-licencji)
11. [Cache offline (grace period)](#11-cache-offline-grace-period)
12. [Testowanie przed wdrożeniem](#12-testowanie-przed-wdrożeniem)
13. [Obfuskacja DLL](#13-obfuskacja-dll)
14. [Rozwiązywanie problemów](#14-rozwiązywanie-problemów)
15. [Checklist wdrożenia](#15-checklist-wdrożenia)

---

## 1. Jak to działa

```
Orchestrator Asset (RT-...)     GitHub Pages (seed.jwt)
         |                              |
         v                              v
   UiPath Robot  ---------------->  FlowRuntime (publiczne API)
   (Invoke Code)    HTTP fetch     ukryta walidacja w DLL
                                           |
                                           v
                              ApiEndpoint, ConnectionString, ...
```

1. Bot pobiera **token** (`RT-2026-CLIENT-001`) z Orchestrator Asset.
2. Biblioteka pobiera **`seed.jwt`** z GitHub Pages (statyczny URL).
3. Weryfikuje podpis envelope (HS256), odszyfrowuje katalog, weryfikuje wpis klienta (`seal`, RSA).
4. Odszyfrowuje payload klienta kluczem zależnym od `tokenId + pepper`.
5. Zwraca dane operacyjne przez **parametry `Out`** z **`FlowRuntime.Activate(...)`**.
6. Operator może **odciąć** bota — watchdog w tle kończy proces robota (`Environment.Exit(1)`).

> **Dla klienta:** w NuGet jest tylko klasa **`FlowRuntime`**. Nie ma dostępu do `Bootstrapper`, kodów `boot-0x*`, override env ani logiki licencji. Watchdog co 15 min ponawia walidację w tle.

---

## 2. Wymagania

| Element | Wymaganie |
|---------|-----------|
| UiPath Studio | 2022.10+ (obsługa .NET 6 / Windows) |
| Robot | Windows, .NET 6 Runtime |
| Orchestrator | Asset typu **Text** na token klienta |
| Sieć | Robot musi mieć HTTPS do URL `seed.jwt` |
| Paczka | `Ops.Runtime.Seed` (.nupkg, target: `net6.0`) |

Token klienta **nie jest** sekretem kryptograficznym — to identyfikator licencji. Sekrety (connection string, API keys) są w zaszyfrowanym payloadzie katalogu.

---

## 3. Instalacja paczki

### 3.1 Gotowa paczka w repo (zalecane)

W repozytorium jest już zbudowany plik:

```
packages/Ops.Runtime.Seed.1.0.0.nupkg
```

UiPath Studio → **Manage Packages** → **Settings** → Local → folder **`packages/`**.

### 3.2 Zbuduj NuGet lokalnie (maintainer)

```bash
./scripts/pack-nuget.sh
```

Kopiuje do `packages/Ops.Runtime.Seed.1.0.0.nupkg` (commituj po zmianach w kodzie).

### 3.2 Dodaj feed w UiPath Studio

1. **Manage Packages** → **Settings** → **User defined packages**
2. Dodaj folder z `.nupkg` (lub feed NuGet wewnętrzny)
3. Zainstaluj **`Ops.Runtime.Seed`** w projekcie automatu

### 3.3 Alternatywa: biblioteka pośrednia

Możesz opakować `FlowRuntime.Activate` w swoją Library UiPath — klient i tak nie zobaczy mechanizmu licencji (jest w `Ops.Runtime.Seed.dll`).

---

## 4. Konfiguracja Orchestratora

### Asset z tokenem

| Pole | Wartość |
|------|---------|
| **Name** | `FLOW_RUNTIME_TOKEN` (zalecane) lub dowolna nazwa |
| **Type** | Text |
| **Value** | `RT-2026-CLIENT-001` (przykład) |
| **Scope** | Folder / Tenant zgodny z procesem |

W procesie pobierz asset standardową aktywnością **Get Asset** i przekaż wartość do Invoke Code.

### Zmienna środowiskowa robota (opcjonalnie)

Biblioteka czyta token także ze zmiennej **`FLOW_RUNTIME_TOKEN`** lub **`APP_BOOT_TOKEN`**. Przy auto-inicjalizacji (ModuleInit) wystarczy ustawić zmienną na maszynie robota — **nie jest to jednak zalecane** w produkcji (Orchestrator Asset daje lepszą kontrolę).

---

## 5. Implementacja w procesie (klient)

**Jeden Invoke Code** — argumenty `In`: `runtimeToken`; `Out`: `apiEndpoint`, `connectionString`, `agentPrompt`, `licenseOwner`, `licenseValidTo`.

```csharp
using Ops.Runtime.Seed;

FlowRuntime.Activate(
    runtimeToken,
    out apiEndpoint,
    out connectionString,
    out agentPrompt,
    out licenseOwner,
    out licenseValidTo);
```

| Wynik | Co się dzieje |
|-------|----------------|
| Token OK | Zwraca dane, watchdog co 15 min w tle |
| Token zły / brak / odcięty | **`Environment.Exit(1)`** — job UiPath pada natychmiast |

Klient **nie ma** innej publicznej metody — nie da się pominąć `Activate`.

---

## 6. Ukryte zabezpieczenie w NuGet

| Element | Klient |
|---------|--------|
| **`FlowRuntime.Activate(...)`** | Jedyna publiczna metoda |
| `Bootstrapper`, kody `boot-0x*` | **Nie widzi** (internal) |
| Watchdog + kill przy odcięciu | Wewnątrz DLL |

Przed wydaniem klientowi: **obfuskuj** DLL (`sample/confuser.example.crproj`) i `./scripts/pack-nuget.sh`.

---

## 7. Pola zwracane przez `Activate` (Out)

| Out (UiPath) | Opis |
|--------------|------|
| `apiEndpoint` | URL API klienta |
| `connectionString` | Connection string (DB / usługa) |
| `agentPrompt` | System prompt dla agenta AI |
| `licenseOwner` | Nazwa właściciela licencji |
| `licenseValidTo` | Data ważności (UTC, format `u`) |

---

## 8. Diagnostyka (tylko operator / maintainer)

Kody `boot-*` są **internal** — widoczne w logach testowych i panelu operatora, nie w publicznym API.

| Kod | Znaczenie |
|-----|-----------|
| `boot-0x12` | Licencja odcięta |
| `boot-0x11` | Token nie w katalogu |
| `boot-0x14` | Wygasła |
| `boot-ok-remote` | OK z Pages |

Klient widzi tylko: job UiPath kończy się natychmiast (brak wyjątku do obsługi w XAML).

---

## 9. Re-walidacja i odcięcie

**Klient nie musi nic robić** — watchdog w DLL co 15 min sprawdza Pages. Po `./scripts/license-api.sh deactivate` robot pada przy kolejnym ticku watchdog (max ~15 min) lub przy restarcie procesu.

---

## 10. Odcięcie i odnowienie licencji

### Operator (panel WWW)

Panel: [https://mikzielinski.github.io/rpalicense/](https://mikzielinski.github.io/rpalicense/)

1. **Odetnij** (`enabled=false`) lub **Odnow** (`enabled=true`)
2. **Przelicz seal** (RSA)
3. **Generuj seed.jwt**
4. **Opublikuj** do repo (GitHub API)

### Operator (CLI / CI)

```bash
# Odcięcie licencji (enabled=false) → bot dostanie boot-0x12
./scripts/license-api.sh deactivate

# Przywrócenie (enabled=true)
./scripts/license-api.sh activate

# Status repo + Pages
./scripts/license-api.sh status
```

Wymaga: `gh` (GitHub CLI) zalogowane z uprawnieniem `contents: write`.

**Uwaga:** GitHub Pages odświeża CDN po commicie — bot może widzieć stary JWT przez 1–4 minuty. Po publikacji poczekaj lub użyj `status`.

### Co widzi bot po odcięciu

1. Kolejne `TryInitialize` pobiera nowy katalog z Pages.
2. Wpis klienta ma `enabled: false`.
3. Biblioteka zwraca **`boot-0x12`** — init zablokowany.

---

## 11. Cache offline (grace period)

- Po udanej walidacji profil jest **cache’owany** lokalnie (domyślnie: `%ProgramData%\OpsRuntime\seed.cache.json`).
- Przy braku sieci biblioteka może użyć cache przez **`GraceDays`** (domyślnie 7 dni).
- Cache **nie** obejmuje odcięcia (`boot-0x12`), wygaśnięcia (`boot-0x14`) ani błędów integralności JWT.

Niestandardowa ścieżka cache (testy / polityka IT):

```
OPS_SEED_CACHE_PATH=C:\RobotData\seed.cache.json
```

---

## 12. Testowanie przed wdrożeniem

### TestApp (symulacja bota)

```bash
dotnet build sample/Ops.Runtime.Seed.TestApp -c Release

$env:FLOW_RUNTIME_TOKEN = "RT-TEST-REPORT-001"
dotnet sample/Ops.Runtime.Seed.TestApp/bin/Release/net6.0/Ops.Runtime.Seed.TestApp.dll
```

### Pełny cykl E2E (API + Pages)

```bash
./scripts/test-license-lifecycle.sh
```

Scenariusze: brak tokenu → niezarejestrowany token → aktywacja API → init OK → deaktywacja API → `boot-0x12`.

### Testy jednostkowe

```bash
dotnet test tests/Ops.Runtime.Seed.Tests -c Release
```

---

## 13. Obfuskacja DLL

Przed produkcją możesz obfuskować `Ops.Runtime.Seed.dll` (ConfuserEx, Dotfuscator). Przykład: `sample/confuser.example.crproj`.

**Zasady:**

1. Obfuskuj **po** `dotnet pack`, na finalnej DLL.
2. **Zawsze** uruchom pełny test init w UiPath po obfuskacji.
3. Nie obfuskuj klucza publicznego RSA — jest osadzony jako stała PEM.

---

## 14. Rozwiązywanie problemów

| Objaw | Przyczyna | Rozwiązanie |
|-------|-----------|-------------|
| `boot-0x01` | Brak tokenu | Get Asset / zmienna procesu |
| `boot-0x11` | Zły token | Sprawdź wpis w katalogu |
| `boot-0x12` | Odcięcie | `./scripts/license-api.sh activate` |
| `boot-0x15` | Zły host | Dodaj `Environment.MachineName` do `hosts` |
| `boot-0x53` | Zły JWT / klucz | Sprawdź `EnvelopeSigningKey` w DLL vs keygen |
| Init wisi długo | Sieć / Pages | Sprawdź URL, timeout 15s; CDN Pages |
| `boot-ok-cache` ciągle | Brak HTTP | Przywróć sieć; sprawdź firewall |
| ModuleInit bez profilu | Init w tle | Wywołaj `FlowRuntime.Activate` explicite na początku procesu |

### Zmienne środowiskowe (override, głównie testy)

| Zmienna | Opis |
|---------|------|
| `FLOW_RUNTIME_TOKEN` | Token klienta (ModuleInit + TestApp) |
| `OPS_SEED_SOURCE_URL` | URL do `seed.jwt` |
| `OPS_SEED_PEPPER` | Pepper AES payloadu |
| `OPS_SEED_ENVELOPE_*` | Parametry envelope JWT |
| `OPS_SEED_PUBLIC_SEAL_KEY_FILE` | Plik PEM klucza publicznego RSA |
| `OPS_SEED_CACHE_PATH` | Ścieżka pliku cache |
| `OPS_SEED_CATALOG_FILE` | Lokalny JWT zamiast HTTP (testy) |

W produkcji stałe są **wbudowane w DLL** — nie ustawiaj `OPS_SEED_*` na robotach produkcyjnych.

---

## 15. Checklist wdrożenia

- [ ] Wygenerowano klucze RSA (`keygen newkeys`)
- [ ] Wystawiono wpis katalogu dla klienta (`keygen issue`)
- [ ] Opublikowano `seed.jwt` na GitHub Pages (`docs/assets/seed.jwt`)
- [ ] Zbudowano i dystrybuowano `.nupkg` z poprawnymi stałymi (`Pepper`, klucze, URL)
- [ ] Utworzono Asset Orchestrator z `RT-...`
- [ ] Dodano `FlowRuntime.Activate` na początku procesu
- [ ] Przetestowano odcięcie (`license-api.sh deactivate`)
- [ ] Obfuskowano DLL przed wysyłką do klienta

---

## Powiązane dokumenty

- [README.md](../README.md) — build, keygen, panel operatora
- [Panel operatora](./index.html) — zarządzanie licencjami w przeglądarce
- `sample/Ops.Runtime.Seed.TestApp` — referencyjna implementacja bota
