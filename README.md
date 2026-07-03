# Ops.Runtime.Seed

Neutralna biblioteka do bootstrapu runtime dla UiPath:
- pobieranie zaszyfrowanego katalogu opakowanego w JWT (JWS + encrypted payload),
- podpis RSA (`seal`) żeby klient nie dopisał sobie wpisu,
- payload szyfrowany AES-GCM zależny od `tokenId + pepper`,
- cache offline z grace period (7 dni),
- autoinit przez zmienną środowiskową (`FLOW_RUNTIME_TOKEN` lub `APP_BOOT_TOKEN`).

## Struktura

- `src/Ops.Runtime.Seed` - biblioteka .NET do spakowania jako `.nupkg`
- `keygen` - narzędzie do:
  - generowania pary kluczy RSA,
  - tworzenia podpisanego wpisu do `catalog.json`,
  - opakowania katalogu do JWT (`wrapjwt`).

## 1) Build biblioteki

```bash
cd src/Ops.Runtime.Seed
dotnet build -c Release
dotnet pack -c Release
```

## 2) Generowanie kluczy podpisu

```bash
cd keygen
dotnet run -- newkeys ./keys
```

Wynik:
- `keys/seal.private.pem` (tylko u Ciebie),
- `keys/seal.public.pem` (wklejasz do `Bootstrapper.cs` -> `PublicSealKeyPem`).

## 3) Wystawienie wpisu katalogu (wewnętrzny JSON)

Przykładowy payload (`payload.json`):

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

Komenda zwraca pojedynczy obiekt JSON - wklej go do:

```json
{
  "entries": [
    { ...wygenerowany wpis... }
  ]
}
```

## 4) Opakowanie katalogu do JWT (do hostowania na GitHub Pages)

```bash
dotnet run -- wrapjwt ./catalog.json "TWOJ-DLUGI-JWT-SIGNING-KEY" "TWOJ-DLUGI-ENVELOPE-PEPPER" "https://twojadomena.pl" "ops-runtime-seed" "2026-12-31T23:59:59Z" > seed.jwt
```

Plik `seed.jwt` hostujesz jako statyczny plik na GitHub Pages / custom domain.
W ruchu sieciowym widać token JWT, ale właściwy katalog jest zaszyfrowany (`blob`).

## 5) UiPath (użycie)

1. Dodaj paczkę `.nupkg` do feedu.
2. Token (`RT-...`) trzymaj jako Asset w Orchestratorze.
3. Na starcie procesu:

```csharp
var profile = Ops.Runtime.Seed.Bootstrapper.Initialize(runtimeTokenFromAsset);
```

4. Używaj wartości z `profile` w procesie (`ApiEndpoint`, `ConnectionString`, `AgentSystemPrompt`).

## 6) Autoinit

Jeśli ustawisz zmienną środowiskową:
- `FLOW_RUNTIME_TOKEN` albo `APP_BOOT_TOKEN`,

to biblioteka spróbuje sama wykonać inicjalizację przy załadowaniu assembly.

## Ważne

- Podmień stałe w `Bootstrapper.cs` przed publikacją:
  - `SourceUrl`
  - `SourceToken` (jeśli prywatne repo)
  - `Pepper`
  - `EnvelopePepper`
  - `EnvelopeSigningKey`
  - `EnvelopeIssuer`
  - `EnvelopeAudience`
  - `PublicSealKeyPem`
- Dla obfuskacji użyj ConfuserEx lub Dotfuscatora dopiero na finalnej DLL i zawsze testuj po obfuskacji w środowisku UiPath.
