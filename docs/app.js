const byId = (id) => document.getElementById(id);
const LS_KEY = "ops-runtime-panel-v2";

/** @type {{ entries: object[] }} */
let catalog = { entries: [] };
/** @type {object[]} */
let auditLog = [];

const state = {
  settings: {},
  defaults: {},
  dirty: false,
  liveLoadedAt: null
};

init();

async function init() {
  await loadDefaults();
  loadSettings();
  bindUi();
  setDefaultDates();
  await refreshFromLive();
  renderAll();
}

async function loadDefaults() {
  try {
    const resp = await fetch("./panel.defaults.json", { cache: "no-store" });
    if (resp.ok) state.defaults = await resp.json();
  } catch {
    state.defaults = {};
  }
}

function defaultSettings() {
  return {
    operatorName: "",
    ghOwner: state.defaults.ghOwner ?? "",
    ghRepo: state.defaults.ghRepo ?? "",
    ghBranch: state.defaults.ghBranch ?? "main",
    ghSeedPath: state.defaults.ghSeedPath ?? "docs/assets/seed.jwt",
    ghAuditPath: state.defaults.ghAuditPath ?? "docs/assets/audit-log.json",
    seedUrl: state.defaults.seedUrl ?? "",
    pepper: "test-pepper-ops-runtime-seed-2026",
    envelopePepper: "test-envelope-pepper-ops-runtime-2026",
    jwtSigningKey: "test-jwt-signing-key-ops-runtime-seed-2026",
    issuer: state.defaults.issuer ?? "",
    audience: state.defaults.audience ?? "ops-runtime-seed",
    sealJwk: ""
  };
}

function loadSettings() {
  try {
    const raw = localStorage.getItem(LS_KEY);
    state.settings = raw ? { ...defaultSettings(), ...JSON.parse(raw) } : defaultSettings();
  } catch {
    state.settings = defaultSettings();
  }
  applySettingsToForm();
}

function saveSettingsFromForm() {
  state.settings = {
    operatorName: byId("cfgOperator").value.trim(),
    ghOwner: byId("cfgGhOwner").value.trim(),
    ghRepo: byId("cfgGhRepo").value.trim(),
    ghBranch: byId("cfgGhBranch").value.trim() || "main",
    ghSeedPath: state.defaults.ghSeedPath ?? "docs/assets/seed.jwt",
    ghAuditPath: state.defaults.ghAuditPath ?? "docs/assets/audit-log.json",
    seedUrl: byId("cfgSeedUrl").value.trim(),
    ghToken: byId("cfgGhToken").value.trim(),
    pepper: byId("cfgPepper").value,
    envelopePepper: byId("cfgEnvelopePepper").value,
    jwtSigningKey: byId("cfgJwtSigningKey").value,
    issuer: byId("cfgIssuer").value.trim(),
    audience: byId("cfgAudience").value.trim(),
    sealJwk: byId("cfgSealJwk").value.trim()
  };
  localStorage.setItem(LS_KEY, JSON.stringify(state.settings));
}

function applySettingsToForm() {
  const s = state.settings;
  byId("cfgOperator").value = s.operatorName ?? "";
  byId("cfgGhOwner").value = s.ghOwner ?? "";
  byId("cfgGhRepo").value = s.ghRepo ?? "";
  byId("cfgGhBranch").value = s.ghBranch ?? "main";
  byId("cfgSeedUrl").value = s.seedUrl ?? "";
  byId("cfgGhToken").value = s.ghToken ?? "";
  byId("cfgPepper").value = s.pepper ?? "";
  byId("cfgEnvelopePepper").value = s.envelopePepper ?? "";
  byId("cfgJwtSigningKey").value = s.jwtSigningKey ?? "";
  byId("cfgIssuer").value = s.issuer ?? "";
  byId("cfgAudience").value = s.audience ?? "";
  byId("cfgSealJwk").value = s.sealJwk ?? "";
}

function bindUi() {
  byId("btnSaveSettings").addEventListener("click", () => {
    saveSettingsFromForm();
    setStatus("settingsStatus", "Ustawienia zapisane w przeglądarce.", "ok");
  });
  byId("btnRefreshLive").addEventListener("click", () => refreshFromLive(true));
  byId("btnPublishAll").addEventListener("click", publishAll);
  byId("btnCreateLicense").addEventListener("click", createLicense);
  byId("btnCheckLive").addEventListener("click", checkLiveStatus);
  byId("btnClearLocalLog").addEventListener("click", () => {
    auditLog = [];
    renderAuditTable();
  });
}

function setDefaultDates() {
  const plusYear = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000);
  byId("newValidTo").value = toLocalInputValue(plusYear);
}

function operatorWho() {
  return state.settings.operatorName?.trim() || "operator";
}

function settingsReady() {
  const s = state.settings;
  return Boolean(
    s.seedUrl && s.pepper && s.envelopePepper && s.jwtSigningKey &&
    s.issuer && s.audience && s.sealJwk
  );
}

function publishReady() {
  const s = state.settings;
  return settingsReady() && s.ghOwner && s.ghRepo && s.ghToken;
}

async function refreshFromLive(manual = false) {
  saveSettingsFromForm();
  if (!settingsReady()) {
    setHeader("Skonfiguruj ustawienia (sekrety + URL seed.jwt).", "warn");
    if (manual) setStatus("publishStatus", "Brak kompletnych ustawień.", "warn");
    return;
  }

  try {
    const jwt = await fetchText(state.settings.seedUrl);
    catalog = await unwrapSeedJwt(jwt);
    state.liveLoadedAt = new Date();
    state.dirty = false;
    await loadAuditLogFromServer();
    setHeader(`Załadowano ${catalog.entries.length} licencji z serwera (${state.liveLoadedAt.toLocaleString()}).`, "ok");
    if (manual) {
      appendAudit("refresh", null, "ok", "Odświeżono katalog z serwera");
      setStatus("publishStatus", "Katalog zsynchronizowany z serwera.", "ok");
    }
  } catch (error) {
    setHeader(`Błąd pobierania z serwera: ${error.message}`, "bad");
    if (manual) setStatus("publishStatus", error.message, "bad");
  }
  renderAll();
}

async function loadAuditLogFromServer() {
  const url = auditLogPublicUrl();
  if (!url) return;
  try {
    const raw = await fetchText(url);
    const parsed = JSON.parse(raw);
    auditLog = Array.isArray(parsed.entries) ? parsed.entries : [];
  } catch {
    auditLog = auditLog.length ? auditLog : [];
  }
}

function auditLogPublicUrl() {
  const seed = state.settings.seedUrl;
  if (!seed) return null;
  return seed.replace(/seed\.jwt(\?.*)?$/, "audit-log.json");
}

async function publishAll() {
  saveSettingsFromForm();
  if (!publishReady()) {
    return setStatus("publishStatus", "Uzupełnij ustawienia: sekrety, PAT, owner, repo.", "warn");
  }
  if (!catalog.entries.length) {
    return setStatus("publishStatus", "Katalog jest pusty — utwórz licencję.", "warn");
  }

  setStatus("publishStatus", "Publikuję…", "");
  try {
    await resealAllEntries();
    const jwt = await buildSeedJwt(catalog);
    await publishGitHubFile(state.settings.ghSeedPath, `${jwt}\n`, "Update seed.jwt (panel)");
    appendAudit("publish", null, "ok", `Opublikowano seed.jwt (${catalog.entries.length} wpisów)`);
    await publishAuditLog();
    state.dirty = false;
    setStatus("publishStatus", "Opublikowano seed.jwt + dziennik. Poczekaj ~1–2 min na Pages.", "ok");
    setHeader(`Opublikowano ${catalog.entries.length} licencji.`, "ok");
  } catch (error) {
    setStatus("publishStatus", `Błąd: ${error.message}`, "bad");
    appendAudit("publish", null, "error", error.message);
  }
  renderAll();
}

async function createLicense() {
  saveSettingsFromForm();
  if (!settingsReady()) {
    return setStatus("createStatus", "Najpierw uzupełnij ustawienia (pepper + JWK).", "warn");
  }

  const tokenId = byId("newTokenId").value.trim();
  const owner = byId("newOwner").value.trim();
  const validLocal = byId("newValidTo").value;
  const hostsRaw = byId("newHosts").value.trim();
  const prompt = byId("newPrompt").value.trim();

  if (!tokenId || !owner || !validLocal) {
    return setStatus("createStatus", "Token, klient i data ważności są wymagane.", "warn");
  }
  if (catalog.entries.some((e) => e.tokenId === tokenId)) {
    return setStatus("createStatus", `Token ${tokenId} już istnieje.`, "warn");
  }

  try {
    const entry = await issueEntry({
      tokenId,
      owner,
      validToUtc: new Date(validLocal).toISOString(),
      hosts: hostsRaw,
      agentPrompt: prompt || "Jestes agentem workflow."
    });
    catalog.entries.push(entry);
    state.dirty = true;
    appendAudit("create", tokenId, "ok", `Utworzono licencję dla ${owner}`);
    setStatus("createStatus", `Utworzono ${tokenId}. Kliknij „Zapisz i opublikuj”.`, "ok");
    byId("newTokenId").value = "";
    renderAll();
  } catch (error) {
    setStatus("createStatus", error.message, "bad");
  }
}

async function issueEntry({ tokenId, owner, validToUtc, hosts, agentPrompt }) {
  const payload = JSON.stringify({
    apiEndpoint: "n/a",
    connectionString: "n/a",
    agentSystemPrompt: agentPrompt
  });
  const key = await deriveSha256Key(`${tokenId}:${state.settings.pepper}`);
  const enc = await encryptAesGcm(key, payload);
  const hostList = hosts
    ? hosts.split(",").map((h) => h.trim().toUpperCase()).filter(Boolean)
    : [];

  const entry = {
    tokenId,
    owner,
    validToUtc,
    enabled: true,
    hosts: hostList,
    blob: toBase64(enc.ciphertext),
    nonce: toBase64(enc.nonce),
    tag: toBase64(enc.tag),
    seal: ""
  };
  entry.seal = await signEntry(entry);
  return entry;
}

async function mutateLicense(tokenId, mode) {
  const entry = catalog.entries.find((e) => e.tokenId === tokenId);
  if (!entry) return;

  if (mode === "disable") {
    entry.enabled = false;
    appendAudit("disable", tokenId, "ok", "Licencja odcięta");
  } else if (mode === "renew") {
    const plusYear = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000);
    entry.enabled = true;
    entry.validToUtc = plusYear.toISOString();
    appendAudit("renew", tokenId, "ok", `Przedłużono do ${entry.validToUtc}`);
  } else if (mode === "delete") {
    catalog.entries = catalog.entries.filter((e) => e.tokenId !== tokenId);
    appendAudit("delete", tokenId, "ok", "Usunięto z katalogu");
  }

  state.dirty = true;
  setStatus("publishStatus", "Zmiana lokalna — kliknij „Zapisz i opublikuj”.", "warn");
  renderAll();
}

async function checkLiveStatus() {
  saveSettingsFromForm();
  const tokenId = byId("checkTokenId").value.trim();
  const machine = byId("checkMachine").value.trim().toUpperCase();

  if (!tokenId) {
    byId("checkResult").textContent = "Podaj token.";
    return;
  }
  if (!settingsReady()) {
    byId("checkResult").textContent = "Uzupełnij ustawienia.";
    return;
  }

  try {
    const jwt = await fetchText(state.settings.seedUrl);
    const liveCatalog = await unwrapSeedJwt(jwt);
    const result = await evaluateToken(liveCatalog, tokenId, machine);
    const text = JSON.stringify(result, null, 2);
    byId("checkResult").textContent = text;
    byId("checkResult").className = `result-box ${result.success ? "ok" : "bad"}`;
    appendAudit(
      "status-check",
      tokenId,
      result.code,
      `machine=${machine || "-"} | live`
    );
    renderAuditTable();
  } catch (error) {
    byId("checkResult").textContent = `Błąd: ${error.message}`;
    byId("checkResult").className = "result-box bad";
    appendAudit("status-check", tokenId, "error", error.message);
    renderAuditTable();
  }
}

async function evaluateToken(catalogDoc, tokenId, machine) {
  const entry = catalogDoc.entries.find((e) => e.tokenId === tokenId);
  if (!entry) {
    return { success: false, code: "boot-0x11", notes: "Nieznany token na serwerze" };
  }
  if (!entry.enabled) {
    return { success: false, code: "boot-0x12", notes: "Licencja odcięta (enabled=false)" };
  }
  const exp = new Date(entry.validToUtc);
  if (Number.isFinite(exp.getTime()) && new Date() > exp) {
    return { success: false, code: "boot-0x14", notes: "Licencja wygasła" };
  }
  const hosts = (entry.hosts ?? []).map((h) => String(h).toUpperCase());
  if (hosts.length > 0 && machine && !hosts.includes(machine)) {
    return { success: false, code: "boot-0x15", notes: `Maszyna ${machine} nie na liście hosts` };
  }
  try {
    const sealOk = await verifyEntrySeal(entry);
    if (!sealOk) {
      return { success: false, code: "boot-0x16", notes: "Nieprawidłowy podpis seal" };
    }
  } catch {
    return { success: false, code: "boot-0x16", notes: "Błąd weryfikacji seal" };
  }
  return {
    success: true,
    code: "boot-ok-remote",
    notes: "Licencja aktywna na serwerze",
    owner: entry.owner,
    validToUtc: entry.validToUtc,
    checkedAtUtc: new Date().toISOString()
  };
}

async function resealAllEntries() {
  for (const e of catalog.entries) {
    e.seal = await signEntry(e);
  }
}

async function signEntry(entry) {
  const jwk = JSON.parse(state.settings.sealJwk);
  const key = await crypto.subtle.importKey(
    "jwk",
    jwk,
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const canonical = canonicalizeEntry(entry);
  const sig = await crypto.subtle.sign(
    "RSASSA-PKCS1-v1_5",
    key,
    new TextEncoder().encode(canonical)
  );
  return toBase64(new Uint8Array(sig));
}

async function verifyEntrySeal(entry) {
  const jwk = JSON.parse(state.settings.sealJwk);
  const publicJwk = { kty: jwk.kty, n: jwk.n, e: jwk.e };
  const key = await crypto.subtle.importKey(
    "jwk",
    publicJwk,
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["verify"]
  );
  const canonical = canonicalizeEntry(entry);
  const sig = fromBase64(entry.seal);
  return crypto.subtle.verify(
    "RSASSA-PKCS1-v1_5",
    key,
    sig,
    new TextEncoder().encode(canonical)
  );
}

async function buildSeedJwt(catalogDoc) {
  const s = state.settings;
  const exp = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000);
  const envelopeKey = await deriveSha256Key(`${s.audience}:${s.envelopePepper}`);
  const enc = await encryptAesGcm(envelopeKey, JSON.stringify(catalogDoc));
  const header = { alg: "HS256", typ: "JWT" };
  const payload = {
    iss: s.issuer,
    aud: s.audience,
    exp: Math.floor(exp.getTime() / 1000),
    nbf: Math.floor(Date.now() / 1000) - 30,
    blob: toBase64(enc.ciphertext),
    nonce: toBase64(enc.nonce),
    tag: toBase64(enc.tag)
  };
  const headerPart = toBase64UrlUtf8(JSON.stringify(header));
  const payloadPart = toBase64UrlUtf8(JSON.stringify(payload));
  const signed = `${headerPart}.${payloadPart}`;
  const sig = await hmacSha256Base64Url(s.jwtSigningKey, signed);
  return `${signed}.${sig}`;
}

async function unwrapSeedJwt(jwt) {
  const s = state.settings;
  const parts = jwt.trim().split(".");
  if (parts.length !== 3) throw new Error("Nieprawidłowy seed.jwt");

  const signed = `${parts[0]}.${parts[1]}`;
  const sigOk = await verifyHs256(signed, parts[2], s.jwtSigningKey);
  if (!sigOk) throw new Error("Nieprawidłowy podpis JWT");

  const claims = JSON.parse(new TextDecoder().decode(fromBase64Url(parts[1])));
  if (claims.iss !== s.issuer || claims.aud !== s.audience) {
    throw new Error("Issuer/audience nie pasuje do ustawień");
  }
  if (claims.exp && Date.now() / 1000 > claims.exp) {
    throw new Error("seed.jwt wygasł (exp)");
  }

  const envelopeKey = await deriveSha256Key(`${s.audience}:${s.envelopePepper}`);
  const plain = await decryptAesGcm(
    envelopeKey,
    fromBase64(claims.blob),
    fromBase64(claims.nonce),
    fromBase64(claims.tag)
  );
  const doc = JSON.parse(plain);
  if (!doc?.entries || !Array.isArray(doc.entries)) {
    throw new Error("Brak entries[] w katalogu");
  }
  return doc;
}

async function publishAuditLog() {
  const body = { entries: auditLog.slice(0, 500) };
  await publishGitHubFile(
    state.settings.ghAuditPath,
    `${JSON.stringify(body, null, 2)}\n`,
    "Update audit-log.json (panel)"
  );
}

async function publishGitHubFile(path, content, message) {
  const s = state.settings;
  const apiUrl = `https://api.github.com/repos/${encodeURIComponent(s.ghOwner)}/${encodeURIComponent(s.ghRepo)}/contents/${path}`;
  const headers = {
    Authorization: `Bearer ${s.ghToken}`,
    Accept: "application/vnd.github+json",
    "Content-Type": "application/json"
  };

  let sha = null;
  const getResp = await fetch(`${apiUrl}?ref=${encodeURIComponent(s.ghBranch)}`, { headers });
  if (getResp.status === 200) {
    sha = (await getResp.json()).sha ?? null;
  } else if (getResp.status !== 404) {
    throw new Error(`GitHub GET HTTP ${getResp.status}`);
  }

  const body = {
    message,
    content: toBase64(new TextEncoder().encode(content)),
    branch: s.ghBranch
  };
  if (sha) body.sha = sha;

  const putResp = await fetch(apiUrl, { method: "PUT", headers, body: JSON.stringify(body) });
  if (!putResp.ok) {
    const txt = await putResp.text();
    throw new Error(`GitHub PUT HTTP ${putResp.status}: ${txt}`);
  }
}

function appendAudit(action, tokenId, code, notes) {
  auditLog.unshift({
    atUtc: new Date().toISOString(),
    who: operatorWho(),
    action,
    tokenId: tokenId ?? "-",
    code: code ?? "-",
    success: code === "ok" || code === "boot-ok-remote",
    notes: notes ?? ""
  });
  if (auditLog.length > 500) auditLog.length = 500;
  renderAuditTable();
}

function licenseStatus(entry) {
  if (!entry.enabled) return { label: "Odcięta", cls: "bad" };
  const exp = new Date(entry.validToUtc);
  if (Number.isFinite(exp.getTime()) && new Date() > exp) return { label: "Wygasła", cls: "warn" };
  return { label: "Aktywna", cls: "ok" };
}

function renderLicenseTable() {
  const tbody = byId("licenseTableBody");
  tbody.innerHTML = "";
  byId("licenseCount").textContent = String(catalog.entries.length);

  for (const e of catalog.entries) {
    const st = licenseStatus(e);
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td><code>${escapeHtml(e.tokenId)}</code></td>
      <td>${escapeHtml(e.owner ?? "-")}</td>
      <td>${escapeHtml(e.validToUtc ?? "-")}</td>
      <td><span class="pill ${st.cls}">${st.label}</span></td>
      <td class="row-actions"></td>
    `;
    const actions = tr.querySelector(".row-actions");
    actions.appendChild(actionBtn("Odnów", () => mutateLicense(e.tokenId, "renew")));
    actions.appendChild(actionBtn("Odetnij", () => mutateLicense(e.tokenId, "disable")));
    actions.appendChild(actionBtn("Usuń", () => {
      if (confirm(`Usunąć licencję ${e.tokenId}?`)) mutateLicense(e.tokenId, "delete");
    }));
    tbody.appendChild(tr);
  }

  if (!catalog.entries.length) {
    const tr = document.createElement("tr");
    tr.innerHTML = `<td colspan="5" class="muted">Brak licencji — utwórz nową lub odśwież z serwera.</td>`;
    tbody.appendChild(tr);
  }
}

function actionBtn(label, onClick) {
  const b = document.createElement("button");
  b.type = "button";
  b.className = "small";
  b.textContent = label;
  b.addEventListener("click", onClick);
  return b;
}

function renderAuditTable() {
  const tbody = byId("auditTableBody");
  tbody.innerHTML = "";
  for (const row of auditLog.slice(0, 100)) {
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td>${escapeHtml(row.atUtc ?? "-")}</td>
      <td>${escapeHtml(row.who ?? "-")}</td>
      <td>${escapeHtml(row.action ?? "-")}</td>
      <td><code>${escapeHtml(row.tokenId ?? "-")}</code></td>
      <td><span class="pill ${row.success ? "ok" : "bad"}">${escapeHtml(row.code ?? "-")}</span></td>
      <td>${escapeHtml(row.notes ?? "")}</td>
    `;
    tbody.appendChild(tr);
  }
  if (!auditLog.length) {
    tbody.innerHTML = `<tr><td colspan="6" class="muted">Brak wpisów — sprawdź status lub opublikuj zmiany.</td></tr>`;
  }
}

function renderAll() {
  renderLicenseTable();
  renderAuditTable();
  byId("catalogRaw").value = JSON.stringify(catalog, null, 2);
  if (state.dirty) {
    setStatus("publishStatus", "Masz nieopublikowane zmiany lokalne.", "warn");
  }
}

function setHeader(text, cls) {
  const el = byId("headerStatus");
  el.textContent = text;
  el.className = `muted ${cls ?? ""}`;
}

function setStatus(id, text, cls) {
  const el = byId(id);
  el.textContent = text;
  el.className = `status ${cls ?? ""}`;
}

function escapeHtml(s) {
  return String(s)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function canonicalizeEntry(entry) {
  const hosts = Array.isArray(entry.hosts)
    ? entry.hosts.map((h) => String(h).trim().toUpperCase()).filter(Boolean).sort()
    : [];
  return [
    String(entry.tokenId ?? "").trim(),
    String(entry.owner ?? "").trim(),
    String(entry.validToUtc ?? "").trim(),
    entry.enabled ? "1" : "0",
    hosts.join(","),
    String(entry.blob ?? "").trim(),
    String(entry.nonce ?? "").trim(),
    String(entry.tag ?? "").trim()
  ].join("|");
}

async function fetchText(url) {
  const resp = await fetch(url, { cache: "no-store" });
  if (!resp.ok) throw new Error(`HTTP ${resp.status} dla ${url}`);
  return resp.text();
}

function toLocalInputValue(date) {
  const pad = (x) => String(x).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

async function deriveSha256Key(input) {
  const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(input));
  return new Uint8Array(hash);
}

async function encryptAesGcm(rawKeyBytes, plaintext) {
  const key = await crypto.subtle.importKey("raw", rawKeyBytes, { name: "AES-GCM" }, false, ["encrypt"]);
  const nonce = crypto.getRandomValues(new Uint8Array(12));
  const combined = new Uint8Array(
    await crypto.subtle.encrypt({ name: "AES-GCM", iv: nonce }, key, new TextEncoder().encode(plaintext))
  );
  return {
    ciphertext: combined.slice(0, combined.length - 16),
    nonce,
    tag: combined.slice(combined.length - 16)
  };
}

async function decryptAesGcm(rawKeyBytes, cipher, nonce, tag) {
  const key = await crypto.subtle.importKey("raw", rawKeyBytes, { name: "AES-GCM" }, false, ["decrypt"]);
  const merged = new Uint8Array(cipher.length + tag.length);
  merged.set(cipher, 0);
  merged.set(tag, cipher.length);
  const plain = await crypto.subtle.decrypt({ name: "AES-GCM", iv: nonce }, key, merged);
  return new TextDecoder().decode(plain);
}

async function hmacSha256Base64Url(secret, message) {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const sig = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(message));
  return toBase64Url(new Uint8Array(sig));
}

async function verifyHs256(message, signatureB64Url, signingKey) {
  const expected = await hmacSha256Base64Url(signingKey, message);
  return expected === signatureB64Url;
}

function toBase64(bytes) {
  let b = "";
  for (let i = 0; i < bytes.length; i += 1) b += String.fromCharCode(bytes[i]);
  return btoa(b);
}

function fromBase64(b64) {
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i += 1) out[i] = bin.charCodeAt(i);
  return out;
}

function toBase64Url(bytes) {
  return toBase64(bytes).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/g, "");
}

function fromBase64Url(value) {
  const s = value.replaceAll("-", "+").replaceAll("_", "/");
  const pad = (4 - (s.length % 4)) % 4;
  return fromBase64(s + "=".repeat(pad));
}

function toBase64UrlUtf8(text) {
  return toBase64Url(new TextEncoder().encode(text));
}
