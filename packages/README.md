# Ops.Runtime.Seed — instalacja w UiPath

Gotowy plik NuGet w tym folderze:

```
Ops.Runtime.Seed.1.0.0.nupkg
```

## Instalacja

1. **Manage Packages** → **Settings** → **Package Sources** → **Add** → **Local**
2. Folder: pełna ścieżka do tego katalogu `packages/`
3. **Manage Packages** → **`Ops.Runtime.Seed`** → Install

## Użycie — jeden Invoke Code

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

Brak lub zły token → proces robota **natychmiast kończy się** (`Environment.Exit(1)`). Innej publicznej metody nie ma.

Manual: [docs/uipath-implementation.md](../docs/uipath-implementation.md)
