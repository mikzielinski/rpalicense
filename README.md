# Ops.Runtime.Seed

Neutralna biblioteka do bootstrapu runtime dla UiPath:
- walidacja wpisu z katalogu JSON hostowanego na GitHub,
- podpis RSA (`seal`) żeby klient nie dopisał sobie wpisu,
- payload szyfrowany AES-GCM zależny od `tokenId + pepper`,
- cache offline z grace period (7 dni),
- autoinit przez zmienną środowiskową (`FLOW_RUNTIME_TOKEN` lub `APP_BOOT_TOKEN`).

## Struktura

- `src/Ops.Runtime.Seed` - biblioteka .NET do spakowania jako `.nupkg`
- `keygen` - narzędzie do:
  - generowania pary kluczy RSA,
  - tworzenia podpisanego wpisu do `catalog.json`.

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

## 3) Wystawienie wpisu katalogu

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

Plik hostuj na GitHub (raw URL) i ustaw `SourceUrl` w `Bootstrapper.cs`.

## 4) UiPath (użycie)

1. Dodaj paczkę `.nupkg` do feedu.
2. Token (`RT-...`) trzymaj jako Asset w Orchestratorze.
3. Na starcie procesu:

```csharp
var profile = Ops.Runtime.Seed.Bootstrapper.Initialize(runtimeTokenFromAsset);
```

4. Używaj wartości z `profile` w procesie (`ApiEndpoint`, `ConnectionString`, `AgentSystemPrompt`).

## 5) Autoinit

Jeśli ustawisz zmienną środowiskową:
- `FLOW_RUNTIME_TOKEN` albo `APP_BOOT_TOKEN`,

to biblioteka spróbuje sama wykonać inicjalizację przy załadowaniu assembly.

## Ważne

- Podmień stałe w `Bootstrapper.cs` przed publikacją:
  - `SourceUrl`
  - `SourceToken` (jeśli prywatne repo)
  - `Pepper`
  - `PublicSealKeyPem`
- Dla obfuskacji użyj ConfuserEx lub Dotfuscatora dopiero na finalnej DLL i zawsze testuj po obfuskacji w środowisku UiPath.
