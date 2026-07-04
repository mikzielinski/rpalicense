# Ops.Runtime.Seed

Neutralna biblioteka bootstrapu runtime dla UiPath:
- pobiera zaszyfrowany katalog opakowany w JWT (JWS + encrypted payload),
- weryfikuje podpis wpisu (`seal`, RSA),
- odszyfrowuje payload klienta (AES-GCM, klucz zależny od `tokenId + pepper`),
- ma cache offline z grace period,
- ma status walidacji (`Bootstrapper.LastCheck`) do logowania w bocie.

## Struktura

- `src/Ops.Runtime.Seed` - biblioteka .NET do paczki `.nupkg`
- `keygen` - CLI do:
  - `newkeys` (RSA keypair),
  - `issue` (podpisany wpis katalogu),
  - `wrapjwt` (opakowanie katalogu do `seed.jwt`),
  - `exportjwk` (konwersja klucza prywatnego do JWK dla panelu WWW)
- `docs` - panel GitHub Pages do operacyjnego zarządzania:
  - walidacja katalogu, symulacja token/machine,
  - zdalne odcięcie/odnowienie,
  - reseal wpisów,
  - generowanie/analiza `seed.jwt`,
  - publikacja `seed.jwt` do repo przez GitHub API.

## 1) Build biblioteki

```bash
cd src/Ops.Runtime.Seed
dotnet build -c Release
dotnet pack -c Release
```

## 2) Klucze RSA

```bash
cd keygen
dotnet run -- newkeys ./keys
```

Wynik:
- `keys/seal.private.pem` (tylko u Ciebie),
- `keys/seal.public.pem` (wklej do `Bootstrapper.cs` -> `PublicSealKeyPem`).

## 3) Wystawienie wpisu katalogu (`catalog.json`)

Przykładowy payload:

```json
{
  "apiEndpoint": "https://api.twoja-firma.pl/v1",
  "connectionString": "Server=...;Database=...;User Id=...;Password=...;",
  "agentSystemPrompt": "Jestes asystentem procesu finansowego."
}
```

Generowanie wpisu:

```bash
dotnet run -- issue ./keys/seal.private.pem "TWOJ-DLUGI-PEPPER" "RT-2026-CLIENT-001" "Klient Sp. z o.o." "2026-12-31T23:59:59Z" "ROBOT01,ROBOT02" ./payload.json
```

Wklej wynik do:

```json
{
  "entries": [
    { "...": "..." }
  ]
}
```

## 4) Opakowanie katalogu do JWT (`seed.jwt`)

```bash
dotnet run -- wrapjwt ./catalog.json "TWOJ-DLUGI-JWT-SIGNING-KEY" "TWOJ-DLUGI-ENVELOPE-PEPPER" "https://twojadomena.pl" "ops-runtime-seed" "2026-12-31T23:59:59Z" > seed.jwt
```

`seed.jwt` hostuj jako statyczny plik na GitHub Pages / custom domain.

## 5) UiPath (metoda główna)

1. Uruchom `release/windows-uipath/INSTALUJ.cmd` (lub `scripts/build-windows.ps1`).
2. Dodaj feed NuGet (`%USERPROFILE%\OpsRuntime\nuget`) i zainstaluj paczkę **Ops.Runtime.Seed**.
3. Token (`RT-...`) trzymaj jako Asset w Orchestratorze lub w zmiennej `FLOW_RUNTIME_TOKEN`.
4. Na starcie procesu (Assign / Invoke Code):

```csharp
var token = runtimeTokenFromAsset;
if (!Ops.Runtime.Seed.Bootstrapper.TryInitialize(token, out var profile))
    throw new System.Exception(Ops.Runtime.Seed.Bootstrapper.LastCheck.Code);
```

5. Używaj danych z `profile` w realnym flow.
6. Loguj stan walidacji:

```csharp
var check = Ops.Runtime.Seed.Bootstrapper.LastCheck;
// check.Success, check.UsedCache, check.Code, check.CheckedAtUtc, check.SourceUrl
```

7. Dla długich procesów: wywołuj `EnsureAuthorized(...)` cyklicznie (np. co 15–30 min). Przy odcięciu/wygasnięciu licencji biblioteka czyści sesję; na Windows domyślnie kończy procesy UiPath (`OPS_SEED_KILL_ON_DENY=1`).

```csharp
Ops.Runtime.Seed.Bootstrapper.EnsureAuthorized(runtimeTokenFromAsset);
```

### Auto-init (opcjonalnie, nadal z paczką NuGet)

Ustaw `FLOW_RUNTIME_TOKEN` na maszynie robota — biblioteka zainicjalizuje się przy załadowaniu DLL (`ModuleInitializer`). W workflow możesz użyć `Bootstrapper.Current` zamiast `TryInitialize`.

### Opcja alternatywna: tryb STEALTH

Bez paczki w projekcie i bez aktywności w XAML — tylko dla maszyny robota (IT/admin): `INSTALUJ-STEALTH.cmd` + `INSTRUKCJA-STEALTH.txt`. Domyślnie używaj metody głównej powyżej.


## 6) Zdalne odcięcie / odnowienie licencji

Mechanizm jest natywnie wspierany:
- odcięcie: `enabled=false`,
- odnowienie: `enabled=true` + nowe `validToUtc`.

Po zmianie wpisu:
1. reseal (`seal`) RSA,
2. wygeneruj nowy `seed.jwt`,
3. opublikuj `seed.jwt` na Pages.

Panel `docs/` prowadzi ten flow krok po kroku.

## 7) Panel GitHub Pages (uproszczony)

1. Włącz GitHub Pages z folderu `docs`.
2. Otwórz opublikowane `index.html`.
3. Workflow operatora (3 kroki):
   - **Krok 1:** wybierz `tokenId` i kliknij `Odetnij` lub `Odnow`, potem `Przelicz seal`.
   - **Krok 2:** kliknij `Generuj seed.jwt`.
   - **Krok 3:** kliknij `Opublikuj seed.jwt do repo`.

Konwersja PEM -> JWK dla panelu:

```bash
cd keygen
dotnet run -- exportjwk ./keys/seal.private.pem
```

## 8) Konfiguracja stałych w bibliotece

W `Bootstrapper.cs` podmień:
- `SourceUrl`
- `SourceToken` (opcjonalnie)
- `Pepper`
- `EnvelopePepper`
- `EnvelopeSigningKey`
- `EnvelopeIssuer`
- `EnvelopeAudience`
- `PublicSealKeyPem`

## Ważne

- Panel przetwarza sekrety lokalnie w przeglądarce, ale jeśli używasz PAT do publikacji, traktuj stronę jak narzędzie administracyjne i nie zostawiaj tokenu w przeglądarce.
- Obfuskację (ConfuserEx / Dotfuscator) rób dopiero na finalnej DLL i zawsze testuj po obfuskacji w środowisku UiPath.
