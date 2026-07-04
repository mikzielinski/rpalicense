# Ops.Runtime.Seed — manual implementacji UiPath

Ten dokument opisuje krok po kroku, jak podłączyć bibliotekę `Ops.Runtime.Seed` do bota UiPath (Studio + Robot + Orchestrator), skonfigurować licencję runtime i obsłużyć zdalne odcięcie bez restartu maszyny.

## Spis treści

1. [Jak to działa](#1-jak-to-działa)
2. [Wymagania](#2-wymagania)
3. [Instalacja paczki](#3-instalacja-paczki)
4. [Konfiguracja Orchestratora](#4-konfiguracja-orchestratora)
5. [Implementacja w procesie](#5-implementacja-w-procesie)
6. [Auto-inicjalizacja (ModuleInit)](#6-auto-inicjalizacja-moduleinit)
7. [Profil runtime i dane klienta](#7-profil-runtime-i-dane-klienta)
8. [Logowanie i kody statusu](#8-logowanie-i-kody-statusu)
9. [Cykliczna re-walidacja](#9-cykliczna-re-walidacja)
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
   UiPath Robot  ---------------->  Bootstrapper
   (Invoke Code)    HTTP fetch     - weryfikuje JWT
                                    - weryfikuje seal (RSA)
                                    - odszyfr. payload (AES)
                                           |
                                           v
                                    RuntimeProfile
                              (API, connection string, prompt)
```

1. Bot pobiera **token** (`RT-2026-CLIENT-001`) z Orchestrator Asset.
2. Biblioteka pobiera **`seed.jwt`** z GitHub Pages (statyczny URL).
3. Weryfikuje podpis envelope (HS256), odszyfrowuje katalog, weryfikuje wpis klienta (`seal`, RSA).
4. Odszyfrowuje payload klienta kluczem zależnym od `tokenId + pepper`.
5. Zwraca **`RuntimeProfile`** z danymi operacyjnymi (endpoint API, connection string itd.).
6. Operator może **odciąć** bota (`enabled=false` w katalogu + publikacja nowego `seed.jwt`) — bot zablokuje się przy kolejnej walidacji (`boot-0x12`).

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

Jeśli wolisz nie wołać `Bootstrapper` bezpośrednio z każdego procesu:

1. Utwórz **Library** (C# Class Library, .NET 6).
2. Dodaj referencję do `Ops.Runtime.Seed`.
3. Opublikuj wrapper, np. `RuntimeGate.EnsureReady(token)`.
4. W procesie UiPath używaj tylko tej biblioteki.

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

## 5. Implementacja w procesie

### Wzorzec zalecany: Invoke Code na początku procesu

```csharp
using Ops.Runtime.Seed;

// token z Get Asset (Orchestrator)
var token = runtimeToken.Trim();

if (!Bootstrapper.TryInitialize(token, out var profile))
{
    var check = Bootstrapper.LastCheck;
    throw new System.Exception(
        $"Licencja runtime niedostępna: {check.Code} " +
        $"(cache={check.UsedCache}, machine={check.Machine})");
}

// profile gotowy — użyj w dalszym flow
System.Console.WriteLine($"OK: {profile.ApiEndpoint}, ważne do {profile.ValidToUtc:u}");
```

### Wariant z wyjątkiem (krótszy)

```csharp
using Ops.Runtime.Seed;

var profile = Bootstrapper.Initialize(runtimeToken);
// rzuca InvalidOperationException z kodem boot-0xNN w Message
```

### Przekazanie profilu do kolejnych aktywności

UiPath Invoke Code nie zwraca obiektów między aktywnościami w prosty sposób — typowe podejścia:

1. **Zapisz pola do zmiennych procesu** tuż po init:

```csharp
apiEndpoint = profile.ApiEndpoint;
connectionString = profile.ConnectionString;
agentPrompt = profile.AgentSystemPrompt;
licenseOwner = profile.Owner;
licenseValidTo = profile.ValidToUtc.ToString("u");
```

2. **Użyj `Bootstrapper.Current`** w późniejszych Invoke Code (po udanym init):

```csharp
var profile = Bootstrapper.Current;
```

---

## 6. Auto-inicjalizacja (ModuleInit)

Paczka `Ops.Runtime.Seed` rejestruje **`[ModuleInitializer]`**. Gdy assembly jest załadowane **i** w środowisku jest `FLOW_RUNTIME_TOKEN` / `APP_BOOT_TOKEN`, biblioteka **w tle** próbuje zainicjalizować profil.

| Zachowanie | Opis |
|------------|------|
| Brak tokenu w env | ModuleInit nic nie robi — init tylko ręczny |
| Token + zdalny katalog | Init w tle (ThreadPool), **nie blokuje** startu procesu |
| Token + lokalny override (testy) | Init w tle |

**Zalecenie produkcyjne:** mimo ModuleInit zawsze wywołuj **`TryInitialize`** na początku procesu — masz pewność, że init zakończył się przed użyciem `Current`, i masz obsługę błędu w jednym miejscu.

---

## 7. Profil runtime i dane klienta

Obiekt **`RuntimeProfile`**:

| Pole | Opis |
|------|------|
| `TokenId` | Id licencji (`RT-...`) |
| `Owner` | Nazwa klienta z katalogu |
| `ApiEndpoint` | URL API procesu |
| `ConnectionString` | Connection string bazy / usługi |
| `AgentSystemPrompt` | Prompt dla agenta AI (jeśli używany) |
| `ValidToUtc` | Data ważności licencji |

Przykład użycia w HTTP Request (pseudo):

```csharp
var profile = Bootstrapper.Current;
// przekaż profile.ApiEndpoint do aktywności HTTP
```

---

## 8. Logowanie i kody statusu

Po każdej walidacji sprawdź **`Bootstrapper.LastCheck`** (`ValidationSnapshot`):

```csharp
var check = Bootstrapper.LastCheck;
// check.Success      — ostatnia walidacja OK
// check.UsedCache    — true = offline grace (brak HTTP, użyto cache)
// check.Code         — np. boot-ok-remote, boot-0x12
// check.TokenId      — token użyty przy sprawdzeniu
// check.Machine      — Environment.MachineName (uppercase)
// check.SourceUrl    — skąd pobrano katalog
// check.CheckedAtUtc — timestamp UTC
// check.Notes        — szczegóły techniczne
```

### Kody `boot-*` (najważniejsze)

| Kod | Znaczenie | Działanie bota |
|-----|-----------|----------------|
| `boot-0x00` | Brak init | Wywołaj `Initialize` / `TryInitialize` |
| `boot-0x01` | Pusty token | Sprawdź Asset Orchestrator |
| `boot-0x11` | Token nie w katalogu | Błędny token / brak wpisu |
| `boot-0x12` | **Licencja odcięta** (`enabled=false`) | Zatrzymaj proces |
| `boot-0x14` | Licencja wygasła | Kontakt z operatorem |
| `boot-0x15` | Maszyna spoza `hosts` | Dodaj robota do listy hosts |
| `boot-0x16` | Nieprawidłowy seal (RSA) | Błąd integralności katalogu |
| `boot-ok-remote` | Walidacja HTTP OK | Normalna praca |
| `boot-ok-cache` | Walidacja z cache (grace) | Praca offline w limicie dni |

Kody `boot-0x5x` dotyczą envelope JWT (HS256, issuer, audience, exp).

---

## 9. Cykliczna re-walidacja

Odcięcie licencji działa **bez restartu robota**, jeśli proces okresowo ponawia walidację.

**Wzorzec:** Parallel / timer co 15–30 minut:

```csharp
if (!Bootstrapper.TryInitialize(token, out _))
{
    var code = Bootstrapper.LastCheck.Code;
    if (code == "boot-0x12" || code == "boot-0x14")
        throw new System.Exception($"Licencja wygasła lub odcięta: {code}");
}
```

Przy `boot-0x12` cache grace **nie** omija blokady — bot zostanie zatrzymany.

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
| ModuleInit bez profilu | Init w tle | Wywołaj `TryInitialize` explicite |

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
- [ ] Dodano Invoke Code z `TryInitialize` na początku procesu
- [ ] Dodano logowanie `LastCheck.Code` do logów robota
- [ ] Skonfigurowano cykliczną re-walidację (15–30 min)
- [ ] Przetestowano odcięcie (`license-api.sh deactivate`) na maszynie testowej
- [ ] Przetestowano proces po obfuskacji (jeśli używana)

---

## Powiązane dokumenty

- [README.md](../README.md) — build, keygen, panel operatora
- [Panel operatora](./index.html) — zarządzanie licencjami w przeglądarce
- `sample/Ops.Runtime.Seed.TestApp` — referencyjna implementacja bota
