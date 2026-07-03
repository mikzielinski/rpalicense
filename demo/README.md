# Demo GitHub Pages + auto-init (.NET)

Program **`demo/Ops.Runtime.Seed.AutoInitDemo`** referencuje `Ops.Runtime.Seed` i **nie wywołuje** `Bootstrapper.Initialize()`.  
Przy starcie procesu wystarczy ustawić token — `ModuleInitializer` sam pobiera `seed.jwt` z GitHub Pages.

## 1. Wygeneruj / odśwież pliki Pages

```bash
chmod +x demo/generate-pages-assets.sh demo/run-autoinit-demo.sh
./demo/generate-pages-assets.sh
```

Powstanie m.in.:

- `docs/assets/seed.jwt` — hostowany na Pages
- `docs/assets/status.html` — strona testu połączenia w przeglądarce
- `demo/pages-demo.env` — zmienne dla demo (gitignored)

## 2. Włącz GitHub Pages

W repo: **Settings → Pages → Source: Deploy from branch → folder `docs`**.

Po deployu:

- Panel: `https://mikzielinski.github.io/rpalicense/`
- Status: `https://mikzielinski.github.io/rpalicense/assets/status.html`
- seed.jwt: `https://mikzielinski.github.io/rpalicense/assets/seed.jwt`

## 3. Uruchom demo .NET (auto-init)

**Lokalnie** (zanim Pages się zdeployuje):

```bash
# terminal 1
cp docs/assets/seed.jwt /tmp/seed-serve/  # lub SEED_JWT_VARIANT=valid ./test/scripts/serve-seed-jwt.sh z docs/assets
mkdir -p /tmp/seed-serve && cp docs/assets/seed.jwt /tmp/seed-serve/
cd /tmp/seed-serve && python3 -m http.server 8765

# terminal 2
export FLOW_RUNTIME_SOURCE_URL=http://127.0.0.1:8765/seed.jwt
export APP_BOOT_TOKEN=RT-DEMO-PAGES-001
dotnet run --project demo/Ops.Runtime.Seed.AutoInitDemo
```

**Po publikacji Pages** (domyślny URL jest w bibliotece):

```bash
./demo/run-autoinit-demo.sh
# albo tylko:
export APP_BOOT_TOKEN=RT-DEMO-PAGES-001
dotnet run --project demo/Ops.Runtime.Seed.AutoInitDemo
```

Oczekiwany wynik: `LastCheck.Success=true`, `Code=boot-ok-remote`, profil z `apiEndpoint`.

## Token demo

`RT-DEMO-PAGES-001` — wpis w katalogu bez ograniczenia `hosts` (dowolna maszyna).
