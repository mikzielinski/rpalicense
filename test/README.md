# Ops.Runtime.Seed — test module

End-to-end validation for the runtime license gate, including a **.NET harness** (runs anywhere) and a **UiPath coded robot** (MacBook Pro / Portable).

## Quick start (local harness — no UiPath required)

```bash
# 1. Generate cryptographic fixtures (keys, catalogs, seed.jwt variants)
chmod +x test/scripts/*.sh
./test/scripts/generate-fixtures.sh

# 2. Run all license scenarios
dotnet run --project test/Ops.Runtime.Seed.TestHarness -- test/fixtures
```

Expected scenarios:

| Scenario | Expected code |
|----------|----------------|
| valid-remote | `boot-ok-remote` |
| empty-token | `boot-0x01` |
| unknown-token | `boot-0x11` |
| disabled-token | `boot-0x12` |
| expired-token | `boot-0x14` |
| wrong-machine | `boot-0x15` |
| invalid-seal | `boot-0x16` |
| cache-fallback | `boot-ok-cache` |
| try-init-failure | `boot-0x11` |

## MacBook Pro + UiPath staging tenant

Staging tenant: `https://staging.uipath.com/mzpocevylrxu/`

> **Note:** At setup time Orchestrator returned *"Automation Cloud Orchestrator is not enabled for this tenant"*. Enable Orchestrator in the tenant admin portal before provisioning.

### Prerequisites (Mac)

1. **.NET 8 SDK** (Apple Silicon): https://dotnet.microsoft.com/download
2. **Node.js 18+** and UiPath CLI:
   ```bash
   npm install -g @uipath/cli
   uip login --tenant <your-tenant>
   ```
3. **UiPath Assistant / Robot** signed in to the same tenant (for local attended runs).

Optional legacy .NET CLI (pack/build only):

```bash
dotnet tool install --global UiPath.CLI.macOS
```

### 1. Generate fixtures + env file

```bash
./test/scripts/generate-fixtures.sh
source test/fixtures/runtime.env
```

On Mac, set the machine alias to match catalog hosts (default `ROBOT01`):

```bash
export FLOW_RUNTIME_TEST_MACHINE="ROBOT01"
# If your Mac hostname differs, pass MachineAlias input to the robot workflow.
```

### 2. Serve `seed.jwt` locally

```bash
# valid catalog (default)
./test/scripts/serve-seed-jwt.sh

# other variants for manual negative tests:
SEED_JWT_VARIANT=disabled ./test/scripts/serve-seed-jwt.sh
SEED_JWT_VARIANT=expired ./test/scripts/serve-seed-jwt.sh
SEED_JWT_VARIANT=bad-seal ./test/scripts/serve-seed-jwt.sh
```

Keep this terminal open. `FLOW_RUNTIME_SOURCE_URL` defaults to `http://127.0.0.1:8765/seed.jwt`.

### 3. Build UiPath coded robot

```bash
./test/scripts/init-uipath-project.sh
```

Or manually:

```bash
cd test/uipath/RuntimeGateTest
uip rpa build
uip rpa test run   # coded test cases
```

### 4. Provision Orchestrator (API)

Create an **External Application** in Orchestrator with scopes:

`OR.Folders OR.Assets OR.Machines OR.Settings OR.Jobs`

Then:

```bash
export UIPATH_CLIENT_ID="..."
export UIPATH_CLIENT_SECRET="..."
export UIPATH_SCOPES="OR.Folders OR.Assets OR.Machines OR.Settings OR.Jobs"
python3 test/scripts/provision-uipath.py
```

This creates (in folder `RuntimeGate`):

- Machine template `MacBookPro-Robot`
- Asset `RuntimeToken` = `RT-2026-CLIENT-001`

Map the machine to your Mac robot in Orchestrator UI, publish the process, and run jobs with input:

```json
{
  "Scenario": "valid",
  "RuntimeToken": "RT-2026-CLIENT-001",
  "MachineAlias": "ROBOT01"
}
```

Negative scenarios: `wrong-token`, `disabled`, `expired`, `wrong-machine`, `empty-token` (swap `seed.jwt` variant when needed).

## Environment variables (testing)

| Variable | Purpose |
|----------|---------|
| `FLOW_RUNTIME_SOURCE_URL` | URL of hosted `seed.jwt` |
| `FLOW_RUNTIME_PEPPER` | Client pepper (must match keygen) |
| `FLOW_RUNTIME_ENVELOPE_*` | JWT envelope signing/decryption |
| `FLOW_RUNTIME_PUBLIC_SEAL_KEY_PEM_FILE` | RSA public key for seal verification |
| `FLOW_RUNTIME_CACHE_PATH` | Override cache file location |
| `FLOW_RUNTIME_TOKEN` / `APP_BOOT_TOKEN` | Auto-init token (ModuleInit) |
| `FLOW_RUNTIME_TEST_TOKEN` | Default token for harness/robot |
| `FLOW_RUNTIME_TEST_MACHINE` | Machine alias (catalog hosts) |

Production builds can omit these; compiled defaults in `Bootstrapper.cs` apply.

## Build commands reference

| Component | Command |
|-----------|---------|
| Library | `dotnet build -c Release -p:LangVersion=latest src/Ops.Runtime.Seed` |
| Keygen | `dotnet run -c Release -p:TargetFramework=net8.0 --project keygen -- <cmd>` |
| Harness | `dotnet run --project test/Ops.Runtime.Seed.TestHarness` |
| UiPath robot | `uip rpa build --project-dir test/uipath/RuntimeGateTest` |
