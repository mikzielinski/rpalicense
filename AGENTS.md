# Ops.Runtime.Seed

Neutral UiPath runtime bootstrap library with operator tooling (`keygen`, GitHub Pages panel).

## Cursor Cloud specific instructions

### Toolchain

- **.NET 8 SDK** is installed at `~/.dotnet` (also supports building `net6.0` library).
- **Node.js 22** + **UiPath CLI (`uip`)** via `npm install -g @uipath/cli` (global prefix `~/.npm-global/bin`).
- **Python 3.12** for fixture/provisioning scripts.

Shell profile adds `DOTNET_ROOT` and `PATH` for dotnet (see `~/.bashrc`).

### Build notes

The library targets `net6.0` but uses C# 11/12 syntax (`[]` collection expressions, raw string literals). Build with:

```bash
dotnet build -c Release -p:LangVersion=latest src/Ops.Runtime.Seed
```

Keygen CLI uses `ExportRSAPrivateKeyPem` (.NET 7+ API). Build/run with:

```bash
cd keygen
dotnet restore -p:TargetFramework=net8.0
dotnet build -c Release -p:TargetFramework=net8.0 --no-restore
dotnet bin/Release/net8.0/SeedForge.dll newkeys ./keys
```

Do **not** use `dotnet run -p:TargetFramework=net8.0` without a prior restore for net8.0 — invoke the built DLL instead.

### Test module

See `test/README.md`. Fast validation without UiPath:

```bash
./test/scripts/generate-fixtures.sh
dotnet run --project test/Ops.Runtime.Seed.TestHarness -- test/fixtures
```

UiPath coded robot project lives under `test/uipath/RuntimeGateTest` (Portable / macOS). `uip rpa create-project` requires `uip login` (interactive browser) — not available in headless cloud VMs.

### Staging tenant

`https://staging.uipath.com/mzpocevylrxu/` — Orchestrator was **not enabled** during agent setup. Use `test/scripts/provision-uipath.py` after enabling Orchestrator and configuring External Application credentials (`UIPATH_CLIENT_ID`, `UIPATH_CLIENT_SECRET`).

### Services

| Service | Port | Start |
|---------|------|-------|
| Operator panel (`docs/`) | 8080 | `cd docs && python3 -m http.server 8080` |
| Local `seed.jwt` (tests) | 8765 | `./test/scripts/serve-seed-jwt.sh` |

No long-running backend exists in-repo; the library fetches `seed.jwt` over HTTP from a static host.
