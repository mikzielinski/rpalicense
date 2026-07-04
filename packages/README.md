# Ops.Runtime.Seed — instalacja w UiPath

Gotowy plik NuGet w tym folderze:

```
Ops.Runtime.Seed.1.0.0.nupkg
```

## Instalacja

1. **Manage Packages** → **Settings** → **Package Sources** → **Add** → **Local**
2. Folder: pełna ścieżka do tego katalogu `packages/`
3. **Manage Packages** → **`Ops.Runtime.Seed`** → Install

## Token (operator — nie w procesie klienta)

- **Opcja A:** zmienna maszynowa `FLOW_RUNTIME_TOKEN=RT-...` na robocie
- **Opcja B:** paczka z wbudowanym tokenem: `./scripts/pack-nuget.sh --runtime-token RT-...`

## Użycie — jeden Invoke Code (bez tokenu w XAML)

```csharp
using Ops.Runtime.Seed;

FlowRuntime.Activate(
    out apiEndpoint,
    out connectionString,
    out agentPrompt,
    out licenseOwner,
    out licenseValidTo);
```

Brak lub zły token → proces robota **natychmiast kończy się** (`Environment.Exit(1)`). Innej publicznej metody nie ma.

Manual: [docs/uipath-implementation.md](../docs/uipath-implementation.md)
