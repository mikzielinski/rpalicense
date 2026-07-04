# Ops.Runtime.Seed — instalacja w UiPath

Gotowy plik NuGet w tym folderze:

```
Ops.Runtime.Seed.1.0.0.nupkg
```

## Instalacja

1. **Manage Packages** → **Settings** → **Package Sources** → **Add** → **Local**
2. Folder: pełna ścieżka do tego katalogu `packages/`
3. **Manage Packages** → **`Ops.Runtime.Seed`** → Install

## Użycie w procesie (jedyne publiczne API)

```csharp
using Ops.Runtime.Seed;

FlowRuntime.Bind(runtimeToken);  // wartość z Orchestrator Asset

var url = FlowRuntime.ApiEndpoint;
var cs  = FlowRuntime.ConnectionString;
```

Mechanizm licencji jest **ukryty w DLL** — klient nie ma dostępu do wyłączenia zabezpieczenia przez publiczne API.

Manual: [docs/uipath-implementation.md](../docs/uipath-implementation.md)
