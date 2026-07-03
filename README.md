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

## 5) UiPath (użycie w bocie)

1. Dodaj `.nupkg` do feedu i zainstaluj paczkę.
2. Token (`RT-...`) trzymaj jako Asset w Orchestratorze.
3. Na starcie procesu:

```csharp
var profile = Ops.Runtime.Seed.Bootstrapper.Initialize(runtimeTokenFromAsset);
```

4. Używaj danych z `profile` w realnym flow.
5. Loguj stan połączenia/walidacji:

```csharp
var check = Ops.Runtime.Seed.Bootstrapper.LastCheck;
// check.Success, check.UsedCache, check.Code, check.CheckedAtUtc, check.SourceUrl
```

6. Wariant bez wyjątku:

```csharp
if (!Ops.Runtime.Seed.Bootstrapper.TryInitialize(runtimeTokenFromAsset, out var _))
{
    var check = Ops.Runtime.Seed.Bootstrapper.LastCheck;
    throw new Exception($"Runtime gate failed: {check.Code}");
}
```

7. Dla długich procesów: wywołuj `TryInitialize(...)` cyklicznie (np. co 15-30 min), aby odcięcie/odnowienie zadziałało bez restartu bota.

## 6) Zdalne odcięcie / odnowienie licencji

Mechanizm jest natywnie wspierany:
- odcięcie: `enabled=false`,
- odnowienie: `enabled=true` + nowe `validToUtc`.

Po zmianie wpisu:
1. reseal (`seal`) RSA,
2. wygeneruj nowy `seed.jwt`,
3. opublikuj `seed.jwt` na Pages.

Panel `docs/` prowadzi ten flow krok po kroku.

## 7) Panel GitHub Pages (operacyjny)

1. Włącz GitHub Pages z folderu `docs`.
2. Otwórz opublikowane `index.html`.
3. Wykonuj operacje:
   - `Zdalne odciecie`,
   - `Odnowienie`,
   - `Przelicz seal`,
   - `Generuj seed.jwt`,
   - `Opublikuj seed.jwt do repo`.

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
