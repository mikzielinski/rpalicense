# Ops.Runtime.Seed — gotowa paczka NuGet

Ten folder zawiera **gotowy plik `.nupkg`** do instalacji w UiPath Studio bez budowania projektu.

## Instalacja w UiPath Studio

1. **Manage Packages** → **Settings** → **Package Sources**
2. **Add** → typ **Local**
3. Folder: sklonuj repo i wskaż `packages/` (pełna ścieżka do tego katalogu)
4. **Manage Packages** → wyszukaj **`Ops.Runtime.Seed`** → **Install**

## Plik

| Paczka | Target |
|--------|--------|
| `Ops.Runtime.Seed.1.0.0.nupkg` | .NET 6.0 (`net6.0`) |

## Odświeżenie paczki (maintainer)

Po zmianach w `src/Ops.Runtime.Seed`:

```bash
./scripts/pack-nuget.sh
```

Skrypt buduje paczkę i kopiuje ją tutaj.

Manual UiPath: [docs/uipath-implementation.md](../docs/uipath-implementation.md)
