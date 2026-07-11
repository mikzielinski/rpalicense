const byId = (id) => document.getElementById(id);
const PANEL_SESSION_KEY = "ops-panel-session-v1";

/** @type {{ entries: object[] }} */
let catalog = { entries: [] };
/** @type {object[]} */
let auditLog = [];
/** @type {object[]} */
let robotEvents = [];

const state = {
  settings: {},
  defaults: {},
  dirty: false,
  liveLoadedAt: null,
  panelUser: null,
  operatorSession: null,
  operatorSessionExp: 0
};

init();

async function init() {
  await loadDefaults();
  loadSettings();
  await ensureSealJwkFromDefaults();
  bindUi();
  await loadOAuthProviders();

  if (consumeOAuthCallbackFromHash()) {
    await enterApp();
    return;
  }

  if (restorePanelSession()) {
    await enterApp();
  } else {
    showLoginScreen();
  }
}

function consumeOAuthCallbackFromHash() {
  const hash = window.location.hash.replace(/^#/, "");
  if (!hash) return false;

  const params = new URLSearchParams(hash);
  const mode = params.get("oauth");
  history.replaceState(null, "", window.location.pathname + window.location.search);

  if (mode === "error") {
    const reason = params.get("reason") ?? "oauth_failed";
    const message = reason === "no_account"
      ? "Brak konta powiązanego z tym GitHub/Google. Poproś admina o dodanie loginu."
      : `Logowanie OAuth nie powiodło się (${reason}).`;
    showLoginScreen();
    setStatus("loginStatus", message, "bad");
    return false;
  }

  if (mode !== "success") return false;

  const sessionToken = params.get("sessionToken");
  const username = params.get("username");
  if (!sessionToken || !username) return false;

  persistPanelSession({
    username,
    isAdmin: params.get("isAdmin") === "1",
    sessionToken,
    expiresAt: params.get("expiresAt")
  });
  return true;
}

async function loadOAuthProviders() {
  const container = byId("oauthProviders");
  try {
    const base = apiBaseUrl();
    if (!base) {
      container.classList.add("hidden");
      return;
    }
    const resp = await fetch(`${base}/v1/panel/oauth/providers`, { headers: { Accept: "application/json" } });
    const json = resp.ok ? await resp.json() : {};
    const show = Boolean(json.github || json.google);
    container.classList.toggle("hidden", !show);
    byId("btnLoginGithub").classList.toggle("hidden", !json.github);
    byId("btnLoginGoogle").classList.toggle("hidden", !json.google);
  } catch {
    container.classList.add("hidden");
  }
}

function startOAuthLogin(provider) {
  const base = apiBaseUrl();
  if (!base) {
    setStatus("loginStatus", "Brak URL API w panel.defaults.json.", "bad");
    return;
  }
  window.location.href = `${base}/v1/panel/oauth/${provider}/start`;
}

async function enterApp() {
  showAppShell();
  setDefaultDates();
  try {
    await refreshFromLive();
  } catch (error) {
    setHeader(`Błąd po zalogowaniu: ${error.message}`, "bad");
  }
  renderAll();
  if (state.panelUser?.isAdmin) {
    await refreshAccountsTable();
    await loadOAuthSetup();
  }
  initRobotPackageConfigurator();
}

function showLoginScreen() {
  byId("loginScreen").classList.remove("hidden");
  byId("appShell").classList.add("hidden");
}

function showAppShell() {
  byId("loginScreen").classList.add("hidden");
  byId("appShell").classList.remove("hidden");
  const user = state.panelUser;
  const headerUser = byId("headerUser");
  if (user) {
    const initials = user.username.slice(0, 2).toUpperCase();
    headerUser.className = `user-badge${user.isAdmin ? " admin" : ""}`;
    headerUser.innerHTML = `<span class="user-avatar" aria-hidden="true">${escapeHtml(initials)}</span><span>${escapeHtml(user.username)}${user.isAdmin ? " · admin" : ""}</span>`;
  } else {
    headerUser.className = "user-badge muted";
    headerUser.textContent = "";
  }
  byId("accountsCard").classList.toggle("hidden", !user?.isAdmin);
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
    seedUrl: state.defaults.seedUrl ?? "",
    apiBaseUrl: state.defaults.apiBaseUrl ?? "",
    pepper: state.defaults.pepper ?? "test-pepper-ops-runtime-seed-2026",
    envelopePepper: state.defaults.envelopePepper ?? "test-envelope-pepper-ops-runtime-2026",
    jwtSigningKey: state.defaults.jwtSigningKey ?? "test-jwt-signing-key-ops-runtime-seed-2026",
    issuer: state.defaults.issuer ?? "",
    audience: state.defaults.audience ?? "ops-runtime-seed",
    sealJwk: state.defaults.sealJwk ?? ""
  };
}

function loadSettings() {
  state.settings = defaultSettings();
}

async function ensureSealJwkFromDefaults() {
  if (state.settings.sealJwk) {
    return;
  }

  const url = state.defaults.sealJwkUrl;
  if (!url) {
    return;
  }

  try {
    const jwk = (await fetchText(url)).trim();
    parseSealJwk(jwk);
    state.settings.sealJwk = jwk;
  } catch (error) {
    console.warn("Nie udało się załadować JWK z panel.defaults.json:", error);
  }
}

function saveSettingsFromForm() {}

function restorePanelSession() {
  try {
    const raw = sessionStorage.getItem(PANEL_SESSION_KEY);
    if (!raw) return false;
    const saved = JSON.parse(raw);
    if (!saved?.sessionToken || !saved?.username) return false;
    if (saved.expiresAt && Date.parse(saved.expiresAt) <= Date.now()) {
      sessionStorage.removeItem(PANEL_SESSION_KEY);
      return false;
    }
    state.panelUser = saved;
    state.operatorSession = saved.sessionToken;
    state.operatorSessionExp = saved.expiresAt ? Date.parse(saved.expiresAt) : Date.now() + 25 * 60 * 1000;
    return true;
  } catch {
    return false;
  }
}

function persistPanelSession(user) {
  state.panelUser = user;
  state.operatorSession = user.sessionToken;
  state.operatorSessionExp = user.expiresAt ? Date.parse(user.expiresAt) : Date.now() + 25 * 60 * 1000;
  sessionStorage.setItem(PANEL_SESSION_KEY, JSON.stringify(user));
}

function clearPanelSession() {
  state.panelUser = null;
  state.operatorSession = null;
  state.operatorSessionExp = 0;
  sessionStorage.removeItem(PANEL_SESSION_KEY);
}

async function loginPanel(username, password) {
  const result = await apiPostPublic("/v1/panel/login", { username, password });
  if (!result.sessionToken) {
    throw new Error("API nie zwróciło sesji.");
  }
  persistPanelSession({
    username: result.username ?? username,
    isAdmin: Boolean(result.isAdmin),
    sessionToken: result.sessionToken,
    expiresAt: result.expiresAt ?? null
  });
}

async function logoutPanel() {
  clearPanelSession();
  catalog = { entries: [] };
  auditLog = [];
  robotEvents = [];
  showLoginScreen();
  setStatus("loginStatus", "", "");
}

function bindUi() {
  byId("loginForm").addEventListener("submit", async (event) => {
    event.preventDefault();
    const username = byId("loginUsername").value.trim();
    const password = byId("loginPassword").value;
    setStatus("loginStatus", "Logowanie…", "");
    try {
      await loginPanel(username, password);
      byId("loginPassword").value = "";
      await enterApp();
      setStatus("loginStatus", "", "");
    } catch (error) {
      setStatus("loginStatus", error.message, "bad");
    }
  });

  byId("btnLoginGithub").addEventListener("click", () => startOAuthLogin("github"));
  byId("btnLoginGoogle").addEventListener("click", () => startOAuthLogin("google"));
  byId("btnLogout").addEventListener("click", () => logoutPanel());
  byId("btnRefreshLive").addEventListener("click", () => refreshFromLive(true));
  byId("btnPublishAll").addEventListener("click", () => publishAll());
  byId("btnCreateLicense").addEventListener("click", createLicense);
  byId("btnCheckLive").addEventListener("click", checkLiveStatus);
  byId("btnCreateAccount").addEventListener("click", createPanelAccount);
  byId("btnLinkOAuth").addEventListener("click", linkPanelAccountOAuth);
  byId("btnSaveOAuthSetup").addEventListener("click", saveOAuthSetup);
  byId("btnDownloadRobotPackage").addEventListener("click", downloadSelectedRobotPackages);
  byId("btnClearLocalLog").addEventListener("click", () => {
    auditLog = [];
    renderAuditTable();
  });
  for (const id of ["robotFilterToken", "robotFilterCode", "robotFilterResult"]) {
    byId(id).addEventListener("input", renderRobotEventsTable);
    byId(id).addEventListener("change", renderRobotEventsTable);
  }
}

function setDefaultDates() {
  const plusYear = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000);
  byId("newValidTo").value = toLocalInputValue(plusYear);
}

function operatorWho() {
  return state.panelUser?.username?.trim() || "operator";
}

function settingsReady() {
  const s = state.settings;
  return Boolean(
    s.apiBaseUrl &&
    s.pepper && s.envelopePepper && s.jwtSigningKey &&
    s.issuer && s.audience && s.sealJwk &&
    state.panelUser?.sessionToken
  );
}

function usesApiPublish() {
  return Boolean(apiBaseUrl() && state.panelUser?.sessionToken);
}

function usesActionsDispatch() {
  return false;
}

function publishReady() {
  const s = state.settings;
  const missing = [];
  if (!state.panelUser?.sessionToken) missing.push("Sesja panelu");
  if (!s.apiBaseUrl) missing.push("URL API");
  if (!s.pepper) missing.push("Pepper");
  if (!s.envelopePepper) missing.push("Envelope pepper");
  if (!s.jwtSigningKey) missing.push("JWT signing key");
  if (!s.issuer) missing.push("Issuer");
  if (!s.audience) missing.push("Audience");
  if (!s.sealJwk) missing.push("JWK RSA");
  state.publishMissing = missing;
  return missing.length === 0;
}

function formatPublishBlockers() {
  if (!state.publishMissing?.length) return "";
  return `Brakuje: ${state.publishMissing.join(", ")}`;
}

async function refreshCatalogFromPages() {
  const jwt = await fetchText(state.settings.seedUrl);
  catalog = await unwrapSeedJwt(jwt);
  state.liveLoadedAt = new Date();
  state.dirty = false;
  await loadAuditLogFromServer();
  await loadRobotEventsFromServer();
}

async function refreshFromLive(manual = false) {
  if (!state.panelUser?.sessionToken) {
    setHeader("Zaloguj się do panelu.", "warn");
    return;
  }
  if (state.dirty && manual) {
    const ok = confirm(
      "Masz nieopublikowane zmiany lokalne. Odświeżenie nadpisze je danymi z serwera. Kontynuować?"
    );
    if (!ok) return;
  }
  if (!settingsReady()) {
    setHeader("Brak konfiguracji panelu lub sesji.", "warn");
    if (manual) setStatus("publishStatus", "Brak kompletnych ustawień.", "warn");
    return;
  }

  try {
    await syncCatalogFromServer();
    setHeader(`Załadowano ${catalog.entries.length} licencji z serwera (${state.liveLoadedAt.toLocaleString()}).`, "ok");
    if (manual) {
      appendAudit("refresh", null, "ok", "Odświeżono katalog z Pages");
      setStatus("publishStatus", "Katalog zsynchronizowany z serwera.", "ok");
    }
  } catch (error) {
    setHeader(`Błąd pobierania z serwera: ${error.message}`, "bad");
    if (manual) setStatus("publishStatus", error.message, "bad");
  }
  renderAll();
}

async function loadAuditLogFromServer() {
  if (usesApiPublish()) {
    try {
      const json = await apiRequest("/v1/audit", "GET");
      auditLog = Array.isArray(json.entries) ? json.entries : [];
    } catch {
      auditLog = auditLog.length ? auditLog : [];
    }
    return;
  }

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

function robotEventsPublicUrl() {
  const seed = state.settings.seedUrl;
  if (!seed) return null;
  return seed.replace(/seed\.jwt(\?.*)?$/, "robot-events.json");
}

async function loadRobotEventsFromServer() {
  if (usesApiPublish()) {
    try {
      const json = await apiRequest("/v1/robot-events", "GET");
      robotEvents = Array.isArray(json.entries) ? json.entries : [];
    } catch {
      robotEvents = robotEvents.length ? robotEvents : [];
    }
    return;
  }

  const url = robotEventsPublicUrl();
  if (!url) return;
  try {
    const raw = await fetchText(url);
    const parsed = JSON.parse(raw);
    robotEvents = Array.isArray(parsed.entries) ? parsed.entries : [];
  } catch {
    robotEvents = robotEvents.length ? robotEvents : [];
  }
}

function apiBaseUrl() {
  return (state.settings.apiBaseUrl ?? "").trim().replace(/\/$/, "");
}

function parseSessionExp(sessionToken) {
  try {
    const payload = JSON.parse(new TextDecoder().decode(fromBase64Url(sessionToken.split(".")[0])));
    return payload.exp ? payload.exp * 1000 : Date.now() + 25 * 60 * 1000;
  } catch {
    return Date.now() + 25 * 60 * 1000;
  }
}

async function apiPostPublic(path, body) {
  const base = apiBaseUrl();
  if (!base) throw new Error("Brak URL API.");

  let resp;
  try {
    resp = await fetch(`${base}${path}`, {
      method: "POST",
      headers: { Accept: "application/json", "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
  } catch (error) {
    throw new Error(
      error?.message === "Failed to fetch"
        ? `Nie można połączyć z API (${base}). Sprawdź URL, CORS i czy serwer działa.`
        : error.message
    );
  }

  const text = await resp.text();
  let json = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    json = null;
  }

  if (!resp.ok) {
    if (resp.status === 401 && json?.error === "invalid_credentials") {
      throw new Error("Nieprawidłowy login lub hasło.");
    }
    const msg = json?.error ?? json?.message ?? text?.slice(0, 300) ?? resp.statusText;
    throw new Error(`API ${resp.status}: ${msg}`);
  }

  return json ?? {};
}

async function ensureOperatorSession() {
  const now = Date.now();
  if (state.operatorSession && state.operatorSessionExp > now + 60_000) {
    return state.operatorSession;
  }
  clearPanelSession();
  showLoginScreen();
  throw new Error("Sesja wygasła — zaloguj się ponownie.");
}

async function apiRequest(path, method, body = null, retried = false) {
  const base = apiBaseUrl();
  if (!base || !state.panelUser?.sessionToken) {
    throw new Error("Brak URL API lub sesji panelu.");
  }

  const session = await ensureOperatorSession();
  const headers = {
    Accept: "application/json",
    Authorization: `Bearer ${session}`
  };
  if (body) headers["Content-Type"] = "application/json";

  let resp;
  try {
    resp = await fetch(`${base}${path}`, {
      method,
      headers,
      body: body ? JSON.stringify(body) : undefined
    });
  } catch (error) {
    throw new Error(
      error?.message === "Failed to fetch"
        ? `Nie można połączyć z API (${base}). Sprawdź URL, CORS i czy serwer działa.`
        : error.message
    );
  }

  const text = await resp.text();
  let json = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    json = null;
  }

  if (resp.status === 401 && !retried) {
    clearPanelSession();
    showLoginScreen();
    throw new Error("Sesja wygasła — zaloguj się ponownie.");
  }

  if (!resp.ok) {
    const msg = json?.error ?? json?.message ?? text?.slice(0, 300) ?? resp.statusText;
    if (resp.status === 401) throw new Error(`Nieprawidłowa sesja: ${msg}`);
    if (resp.status === 403) throw new Error(`API 403 — brak uprawnień: ${msg}`);
    throw new Error(`API ${resp.status}: ${msg}`);
  }

  return json ?? {};
}

async function fetchSeedForPublish() {
  if (usesApiPublish()) {
    const json = await apiRequest("/v1/catalog", "GET");
    return { sha: null, text: json.jwt ?? "" };
  }
  if (usesActionsDispatch()) {
    const jwt = await fetchText(state.settings.seedUrl);
    return { sha: null, text: jwt };
  }
  return fetchGitHubFileMeta(state.settings.ghSeedPath);
}

async function dispatchLicenseOps(eventType, clientPayload) {
  const s = state.settings;
  const url = `https://api.github.com/repos/${encodeURIComponent(s.ghOwner)}/${encodeURIComponent(s.ghRepo)}/dispatches`;
  await githubRequest(url, "POST", {
    event_type: eventType,
    client_payload: clientPayload
  });
}

async function publishAllViaActions(options = {}) {
  const mutation = options.mutation ?? null;
  const operatorKey = state.settings.apiKey?.trim();

  if (mutation || !state.dirty) {
    const jwt = await fetchText(state.settings.seedUrl);
    catalog = await unwrapSeedJwt(jwt);
    if (mutation) {
      applyCatalogMutation(mutation);
    }
  }

  if (mutation && mutation.mode !== "create" && mutation.mode !== "delete") {
    if (!catalog.entries.some((e) => e.tokenId === mutation.tokenId)) {
      throw new Error(`Token ${mutation.tokenId} nie istnieje w katalogu na serwerze.`);
    }
  }

  if (!catalog.entries.length) {
    const msg = "Katalog jest pusty — utwórz licencję lub odśwież z serwera.";
    setStatus("publishStatus", msg, "warn");
    return { ok: false, error: msg };
  }

  const message = options.commitMessage ?? "Update seed.jwt (panel)";
  setStatus("publishStatus", "Wysyłam do GitHub Actions (Pages)…", "");

  await resealAllEntries();
  const jwt = await buildSeedJwt(catalog);

  await dispatchLicenseOps("publish-seed", {
    apiKey: operatorKey,
    jwt,
    message
  });

  if (mutation) {
    appendAuditForMutation(mutation);
  }
  appendAudit("publish", null, "ok", "seed.jwt → GitHub Actions");

  try {
    await dispatchLicenseOps("publish-audit", {
      apiKey: operatorKey,
      entries: auditLog.slice(0, 500),
      message: "Update audit-log.json (panel)"
    });
  } catch (auditError) {
    appendAudit("publish-audit", null, "warn", auditError.message);
  }

  state.dirty = false;
  const msg = "Wysłano do GitHub Actions. Pages zaktualizuje się za ~1–2 min.";
  setStatus("publishStatus", msg, "ok");
  setHeader(`Opublikowano ${catalog.entries.length} licencji (workflow na GitHub).`, "ok");
  renderAll();
  return { ok: true };
}

async function publishAllViaApi(options = {}) {
  const mutation = options.mutation ?? null;

  const meta = await fetchSeedForPublish();
  if (mutation) {
    catalog = await unwrapSeedJwt(meta.text);
    applyCatalogMutation(mutation);
  } else if (!state.dirty) {
    catalog = await unwrapSeedJwt(meta.text);
  }

  if (mutation && mutation.mode !== "create" && mutation.mode !== "delete") {
    if (!catalog.entries.some((e) => e.tokenId === mutation.tokenId)) {
      throw new Error(`Token ${mutation.tokenId} nie istnieje w katalogu na serwerze.`);
    }
  }

  if (!catalog.entries.length) {
    const msg = "Katalog jest pusty — utwórz licencję lub odśwież z serwera.";
    setStatus("publishStatus", msg, "warn");
    return { ok: false, error: msg };
  }

  setStatus("publishStatus", "Publikuję seed.jwt przez API…", "");
  await resealAllEntries();
  const jwt = await buildSeedJwt(catalog);
  const seedResult = await apiRequest("/v1/catalog/publish", "POST", {
    jwt,
    message: options.commitMessage ?? "Update seed.jwt (panel)"
  });

  if (mutation) {
    appendAuditForMutation(mutation);
  }
  appendAudit("publish", null, "ok", `seed.jwt → ${String(seedResult.revision ?? "ok").slice(0, 7)}`);

  try {
    await apiRequest("/v1/audit", "POST", { entries: auditLog.slice(0, 500) });
  } catch (auditError) {
    appendAudit("publish-audit", null, "warn", auditError.message);
  }

  state.dirty = false;
  const msg = `Opublikowano przez API (rev ${String(seedResult.revision ?? "?").slice(0, 7)}). Pages za ~1–2 min.`;
  setStatus("publishStatus", msg, "ok");
  setHeader(`Opublikowano ${catalog.entries.length} licencji na serwerze.`, "ok");
  renderAll();
  return { ok: true };
}

async function publishAll(options = {}) {
  saveSettingsFromForm();
  if (!publishReady()) {
    const msg = formatPublishBlockers() || "Uzupełnij ustawienia.";
    setStatus("publishStatus", msg, "warn");
    return { ok: false, error: msg };
  }

  if (usesApiPublish()) {
    setPublishing(true);
    try {
      return await publishAllViaApi(options);
    } catch (error) {
      try {
        await syncCatalogFromServer();
        renderAll();
      } catch {
        // keep local state
      }
      const msg = `Błąd publikacji API: ${error.message}`;
      setStatus("publishStatus", msg, "bad");
      appendAudit("publish", null, "error", error.message);
      renderAuditTable();
      return { ok: false, error: error.message };
    } finally {
      setPublishing(false);
    }
  }

  if (usesActionsDispatch()) {
    setPublishing(true);
    try {
      return await publishAllViaActions(options);
    } catch (error) {
      try {
        await refreshCatalogFromPages();
        renderAll();
      } catch {
        // keep local state
      }
      const msg = `Błąd GitHub Actions: ${error.message}`;
      setStatus("publishStatus", msg, "bad");
      appendAudit("publish", null, "error", error.message);
      renderAuditTable();
      return { ok: false, error: error.message };
    } finally {
      setPublishing(false);
    }
  }

  const mutation = options.mutation ?? null;
  const maxAttempts = 8;

  setPublishing(true);

  try {
    for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
      try {
        let seedSha = null;

        if (publishReady()) {
          const meta = await fetchGitHubFileMeta(state.settings.ghSeedPath);
          seedSha = meta.sha;
          if (mutation) {
            catalog = await unwrapSeedJwt(meta.text);
            applyCatalogMutation(mutation);
          } else if (!state.dirty) {
            catalog = await unwrapSeedJwt(meta.text);
          }
        }

        if (mutation && mutation.mode !== "create" && mutation.mode !== "delete") {
          if (!catalog.entries.some((e) => e.tokenId === mutation.tokenId)) {
            throw new Error(`Token ${mutation.tokenId} nie istnieje w katalogu na serwerze.`);
          }
        }

        if (!catalog.entries.length) {
          const msg = "Katalog jest pusty — utwórz licencję lub odśwież z serwera.";
          setStatus("publishStatus", msg, "warn");
          return { ok: false, error: msg };
        }

        const statusMsg =
          attempt === 0
            ? "Publikuję seed.jwt na GitHub…"
            : `Konflikt wersji — ponawiam publikację (${attempt + 1}/${maxAttempts})…`;
        setStatus("publishStatus", statusMsg, attempt > 0 ? "warn" : "");

        await resealAllEntries();
        const jwt = await buildSeedJwt(catalog);
        const seedResult = await putGitHubFileOnce(
          state.settings.ghSeedPath,
          `${jwt}\n`,
          options.commitMessage ?? "Update seed.jwt (panel)",
          seedSha
        );

        if (mutation) {
          appendAuditForMutation(mutation);
        }
        appendAudit("publish", null, "ok", `seed.jwt → commit ${seedResult.sha?.slice(0, 7) ?? "ok"}`);

        try {
          await publishAuditLog();
        } catch (auditError) {
          appendAudit("publish-audit", null, "warn", auditError.message);
        }

        state.dirty = false;
        const msg = `Opublikowano seed.jwt (commit ${seedResult.sha?.slice(0, 7) ?? "?"}). Pages za ~1–2 min.`;
        setStatus("publishStatus", msg, "ok");
        setHeader(`Opublikowano ${catalog.entries.length} licencji na serwerze.`, "ok");
        renderAll();
        return { ok: true };
      } catch (error) {
        const isConflict = String(error.message).includes("HTTP 409");
        if (isConflict && attempt < maxAttempts - 1) {
          await new Promise((resolve) => setTimeout(resolve, 600 * (attempt + 1)));
          continue;
        }
        throw error;
      }
    }

    throw new Error("Przekroczono limit prób publikacji.");
  } catch (error) {
    if (publishReady()) {
      try {
        await syncCatalogFromGitHubApi();
        renderAll();
      } catch {
        // keep local state if resync fails
      }
    }
    const msg = `Błąd publikacji: ${error.message}`;
    setStatus("publishStatus", msg, "bad");
    appendAudit("publish", null, "error", error.message);
    renderAuditTable();
    return { ok: false, error: error.message };
  } finally {
    setPublishing(false);
  }
}

function applyCatalogMutation(mutation) {
  const { mode, tokenId, entry: newEntry } = mutation;
  if (mode === "create") {
    if (newEntry && !catalog.entries.some((e) => e.tokenId === newEntry.tokenId)) {
      catalog.entries.push(newEntry);
    }
    return;
  }

  const entry = catalog.entries.find((e) => e.tokenId === tokenId);
  if (mode === "disable" && entry) {
    entry.enabled = false;
  } else if (mode === "renew" && entry) {
    entry.enabled = true;
    entry.validToUtc = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000).toISOString();
  } else if (mode === "delete") {
    catalog.entries = catalog.entries.filter((e) => e.tokenId !== tokenId);
  }
}

function appendAuditForMutation(mutation) {
  const { mode, tokenId, entry } = mutation;
  if (mode === "disable") {
    appendAudit("disable", tokenId, "ok", "Licencja odcięta");
  } else if (mode === "renew") {
    const renewed = catalog.entries.find((e) => e.tokenId === tokenId);
    appendAudit("renew", tokenId, "ok", `Przedłużono do ${renewed?.validToUtc ?? "?"}`);
  } else if (mode === "delete") {
    appendAudit("delete", tokenId, "ok", "Usunięto z katalogu");
  } else if (mode === "create") {
    appendAudit("create", tokenId, "ok", `Utworzono licencję dla ${entry?.owner ?? "?"}`);
  }
}

function parseSealJwk(raw) {
  if (!raw?.trim()) {
    throw new Error("Brak klucza JWK w ustawieniach. Kliknij „Załaduj klucz testowy” lub wklej pełny exportjwk.");
  }
  let text = raw.trim().replace(/^\uFEFF/, "");
  if (text.length < 2000) {
    throw new Error(
      `Klucz JWK za krótki (${text.length} znaków). Pełny klucz ma ok. 2400 znaków — wklejony fragment jest ucięty.`
    );
  }
  try {
    const jwk = JSON.parse(text);
    if (!jwk.kty || !jwk.n || !jwk.e || !jwk.d) {
      throw new Error("JWK musi być kluczem prywatnym RSA (pola kty, n, e, d).");
    }
    return jwk;
  } catch (error) {
    throw new Error(
      `Nieprawidłowy JWK JSON: ${error.message}. Użyj „Załaduj klucz testowy” lub wklej cały wynik exportjwk jako jedną linię.`
    );
  }
}

async function refreshAccountsTable() {
  if (!state.panelUser?.isAdmin) return;
  try {
    const json = await apiRequest("/v1/panel/accounts", "GET");
    const accounts = Array.isArray(json.accounts) ? json.accounts : [];
    const body = byId("accountsTableBody");
    body.innerHTML = accounts.map((account) => {
      const isSelf = account.username === state.panelUser.username;
      const roleCls = account.isAdmin ? "admin" : "operator";
      const roleLabel = account.isAdmin ? "Administrator" : "Operator";
      const deleteBtn = isSelf
        ? ""
        : `<button type="button" class="btn-sm btn-danger" data-delete-account="${escapeHtml(account.username)}">Usuń</button>`;
      const linkBtn = `<button type="button" class="btn-sm btn-ghost" data-link-account="${escapeHtml(account.username)}" data-github="${escapeHtml(account.githubLogin ?? "")}" data-google="${escapeHtml(account.googleEmail ?? "")}">Powiąż OAuth</button>`;
      return `<tr>
        <td><strong>${escapeHtml(account.username)}</strong></td>
        <td><span class="role-pill ${roleCls}">${roleLabel}</span></td>
        <td>${account.githubLogin ? `<code>${escapeHtml(account.githubLogin)}</code>` : '<span class="muted">—</span>'}</td>
        <td>${account.googleEmail ? escapeHtml(account.googleEmail) : '<span class="muted">—</span>'}</td>
        <td class="muted">${escapeHtml(account.createdAt ?? "")}</td>
        <td class="cell-actions">${linkBtn} ${deleteBtn}</td>
      </tr>`;
    }).join("");

    body.querySelectorAll("[data-link-account]").forEach((button) => {
      button.addEventListener("click", () => {
        byId("linkAccountUsername").value = button.getAttribute("data-link-account") ?? "";
        byId("linkAccountGithub").value = button.getAttribute("data-github") ?? "";
        byId("linkAccountGoogle").value = button.getAttribute("data-google") ?? "";
        byId("linkAccountGithub").focus();
      });
    });

    body.querySelectorAll("[data-delete-account]").forEach((button) => {
      button.addEventListener("click", async () => {
        const username = button.getAttribute("data-delete-account");
        if (!username || !confirm(`Usunąć konto ${username}?`)) return;
        try {
          await apiRequest(`/v1/panel/accounts/${encodeURIComponent(username)}`, "DELETE");
          setStatus("accountsStatus", `Usunięto konto ${username}.`, "ok");
          await refreshAccountsTable();
        } catch (error) {
          setStatus("accountsStatus", error.message, "bad");
        }
      });
    });
  } catch (error) {
    setStatus("accountsStatus", error.message, "bad");
  }
}

function detectPanelPublicUrl() {
  const path = window.location.pathname.replace(/\/index\.html?$/i, "").replace(/\/$/, "");
  return `${window.location.origin}${path}`;
}

function fillOAuthProviderForm(prefix, provider) {
  byId(`${prefix}Enabled`).checked = Boolean(provider?.enabled);
  byId(`${prefix}ClientId`).value = provider?.clientId ?? "";
  byId(`${prefix}ClientSecret`).value = "";
  byId(`${prefix}Callback`).value = provider?.callbackUrl ?? "";
  const hint = byId(`${prefix}SecretHint`);
  if (provider?.secretConfigured) {
    hint.textContent = `Zapisany secret: ${provider.secretHint || "••••"}`;
  } else {
    hint.textContent = "Secret nie jest jeszcze zapisany.";
  }
}

async function loadOAuthSetup() {
  if (!state.panelUser?.isAdmin) return;
  try {
    const setup = await apiRequest("/v1/panel/oauth/setup", "GET");
    byId("oauthPanelUrl").value = setup.panelPublicUrl || detectPanelPublicUrl();
    byId("oauthApiUrl").value = setup.apiPublicUrl || apiBaseUrl();
    fillOAuthProviderForm("oauthGithub", setup.github);
    fillOAuthProviderForm("oauthGoogle", setup.google);
  } catch (error) {
    byId("oauthPanelUrl").value = detectPanelPublicUrl();
    byId("oauthApiUrl").value = apiBaseUrl();
    setStatus("accountsStatus", `OAuth: ${error.message}`, "warn");
  }
}

async function saveOAuthSetup() {
  if (!state.panelUser?.isAdmin) {
    setStatus("accountsStatus", "Brak uprawnień administratora.", "bad");
    return;
  }

  const panelPublicUrl = byId("oauthPanelUrl").value.trim();
  const apiPublicUrl = byId("oauthApiUrl").value.trim();
  if (!panelPublicUrl || !apiPublicUrl) {
    setStatus("accountsStatus", "Podaj URL panelu i URL API.", "warn");
    return;
  }

  const body = {
    panelPublicUrl,
    apiPublicUrl,
    github: {
      enabled: byId("oauthGithubEnabled").checked,
      clientId: byId("oauthGithubClientId").value.trim(),
      clientSecret: byId("oauthGithubClientSecret").value
    },
    google: {
      enabled: byId("oauthGoogleEnabled").checked,
      clientId: byId("oauthGoogleClientId").value.trim(),
      clientSecret: byId("oauthGoogleClientSecret").value
    }
  };

  try {
    await apiRequest("/v1/panel/oauth/setup", "PUT", body);
    byId("oauthGithubClientSecret").value = "";
    byId("oauthGoogleClientSecret").value = "";
    setStatus("accountsStatus", "Zapisano konfigurację OAuth.", "ok");
    await loadOAuthSetup();
    await loadOAuthProviders();
  } catch (error) {
    setStatus("accountsStatus", error.message, "bad");
  }
}

async function linkPanelAccountOAuth() {
  if (!state.panelUser?.isAdmin) {
    setStatus("accountsStatus", "Brak uprawnień administratora.", "bad");
    return;
  }

  const username = byId("linkAccountUsername").value.trim();
  const githubLogin = byId("linkAccountGithub").value.trim();
  const googleEmail = byId("linkAccountGoogle").value.trim().toLowerCase();
  if (username.length < 3) {
    setStatus("accountsStatus", "Podaj login istniejącego konta.", "warn");
    return;
  }
  if (!githubLogin && !googleEmail) {
    setStatus("accountsStatus", "Podaj GitHub login lub Google email.", "warn");
    return;
  }

  try {
    const body = {};
    if (githubLogin) body.githubLogin = githubLogin;
    if (googleEmail) body.googleEmail = googleEmail;
    await apiRequest(`/v1/panel/accounts/${encodeURIComponent(username)}`, "PATCH", body);
    setStatus("accountsStatus", `Powiązano OAuth dla ${username}.`, "ok");
    await refreshAccountsTable();
  } catch (error) {
    setStatus("accountsStatus", error.message, "bad");
  }
}

async function createPanelAccount() {
  if (!state.panelUser?.isAdmin) {
    setStatus("accountsStatus", "Brak uprawnień administratora.", "bad");
    return;
  }

  const username = byId("newAccountUsername").value.trim();
  const password = byId("newAccountPassword").value;
  const githubLogin = byId("newAccountGithub").value.trim();
  const googleEmail = byId("newAccountGoogle").value.trim().toLowerCase();
  const isAdmin = byId("newAccountIsAdmin").value === "1";
  const hasOAuth = Boolean(githubLogin || googleEmail);
  if (username.length < 3 || (password.length < 8 && !hasOAuth)) {
    setStatus("accountsStatus", "Login min. 3 znaki; hasło min. 8 lub podaj GitHub/Google.", "warn");
    return;
  }

  try {
    await apiRequest("/v1/panel/accounts", "POST", { username, password, isAdmin, githubLogin, googleEmail });
    byId("newAccountUsername").value = "";
    byId("newAccountPassword").value = "";
    byId("newAccountGithub").value = "";
    byId("newAccountGoogle").value = "";
    byId("newAccountIsAdmin").value = "0";
    setStatus("accountsStatus", `Dodano konto ${username}.`, "ok");
    await refreshAccountsTable();
  } catch (error) {
    setStatus("accountsStatus", error.message, "bad");
  }
}

async function applyChangeAndPublish(actionLabel, tokenId, options = {}) {
  renderAll();
  if (!publishReady()) {
    setStatus(
      "publishStatus",
      `${actionLabel}: zapisano lokalnie. Uzupełnij klucz API (lub PAT) i kliknij „Zapisz i opublikuj”.`,
      "warn"
    );
    return false;
  }
  const result = await publishAll({
    commitMessage: `Panel: ${actionLabel} ${tokenId}`,
    mutation: options.mutation ?? null
  });
  if (result.ok) {
    setStatus("publishStatus", `${actionLabel} — opublikowano na serwerze.`, "ok");
  }
  return result.ok;
}

let publishing = false;

function setPublishing(on) {
  publishing = on;
  for (const id of ["btnPublishAll", "btnRefreshLive", "btnCreateLicense"]) {
    const el = byId(id);
    if (el) el.disabled = on;
  }
  document.querySelectorAll("#licenseTableBody button").forEach((b) => {
    b.disabled = on;
  });
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

  if (publishReady()) {
    try {
      await syncCatalogFromGitHubApi();
      renderAll();
    } catch (error) {
      return setStatus("createStatus", `Sync GitHub: ${error.message}`, "bad");
    }
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
    byId("newTokenId").value = "";
    const ok = await applyChangeAndPublish("Utworzono", tokenId, {
      mutation: { mode: "create", tokenId, entry }
    });
    setStatus(
      "createStatus",
      ok ? `Utworzono i opublikowano ${tokenId}.` : `Nie udało się opublikować ${tokenId}.`,
      ok ? "ok" : "bad"
    );
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
  if (publishing) return;

  const labels = { disable: "Odcięcie", renew: "Odnowienie", delete: "Usunięcie" };
  const verbs = {
    disable: `Odetnąć licencję ${tokenId} i opublikować na serwerze?`,
    renew: `Odnowić licencję ${tokenId} i opublikować na serwerze?`,
    delete: `Usunąć licencję ${tokenId} z katalogu i opublikować?`
  };
  if (!confirm(verbs[mode])) return;

  if (!publishReady()) {
    const entry = catalog.entries.find((e) => e.tokenId === tokenId);
    if (!entry && mode !== "delete") return;

    if (mode === "disable" && entry) {
      entry.enabled = false;
      entry.seal = await signEntry(entry);
      appendAudit("disable", tokenId, "ok", "Licencja odcięta (lokalnie)");
    } else if (mode === "renew" && entry) {
      entry.enabled = true;
      entry.validToUtc = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000).toISOString();
      entry.seal = await signEntry(entry);
      appendAudit("renew", tokenId, "ok", `Przedłużono lokalnie do ${entry.validToUtc}`);
    } else if (mode === "delete") {
      catalog.entries = catalog.entries.filter((e) => e.tokenId !== tokenId);
      appendAudit("delete", tokenId, "ok", "Usunięto lokalnie z katalogu");
    }
    state.dirty = true;
    renderAll();
    setStatus(
      "publishStatus",
      `${labels[mode]}: zapisano lokalnie. Uzupełnij PAT i kliknij „Zapisz i opublikuj”.`,
      "warn"
    );
    return;
  }

  await applyChangeAndPublish(labels[mode], tokenId, {
    mutation: { mode, tokenId }
  });
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
    let liveCatalog;
    if (usesApiPublish()) {
      const meta = await fetchSeedForPublish();
      liveCatalog = await unwrapSeedJwt(meta.text);
    } else {
      const jwt = await fetchText(state.settings.seedUrl);
      liveCatalog = await unwrapSeedJwt(jwt);
    }
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
  const jwk = parseSealJwk(state.settings.sealJwk);
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
  const jwk = parseSealJwk(state.settings.sealJwk);
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

function githubContentsApiUrl(path) {
  const encodedPath = path.split("/").map(encodeURIComponent).join("/");
  return `https://api.github.com/repos/${encodeURIComponent(state.settings.ghOwner)}/${encodeURIComponent(state.settings.ghRepo)}/contents/${encodedPath}`;
}

async function fetchGitHubFileMeta(path) {
  const apiUrl = githubContentsApiUrl(path);
  const ref = encodeURIComponent(state.settings.ghBranch);
  const json = await githubRequest(`${apiUrl}?ref=${ref}&_=${Date.now()}`, "GET", null, { raw: true });

  const sha = json?.sha;
  if (!sha) throw new Error(`Brak SHA dla ${path} na branchu ${state.settings.ghBranch}`);

  let text;
  if (typeof json.content === "string" && json.content.length > 0) {
    const b64 = json.content.replace(/\s/g, "");
    text = new TextDecoder().decode(fromBase64(b64));
  } else if (json.download_url) {
    text = await fetchText(`${json.download_url}?_=${Date.now()}`);
  } else {
    throw new Error(`Brak treści pliku ${path} w odpowiedzi GitHub API`);
  }

  return { sha, text };
}

async function syncCatalogFromServer() {
  saveSettingsFromForm();
  if (!settingsReady()) {
    throw new Error("Uzupełnij sekrety kryptograficzne w ustawieniach.");
  }

  if (usesApiPublish()) {
    const meta = await fetchSeedForPublish();
    catalog = await unwrapSeedJwt(meta.text);
  } else if (usesActionsDispatch()) {
    const jwt = await fetchText(state.settings.seedUrl);
    catalog = await unwrapSeedJwt(jwt);
  } else {
    if (!state.settings.ghOwner || !state.settings.ghRepo || !state.settings.ghToken) {
      throw new Error("Brak konfiguracji GitHub (owner/repo/PAT).");
    }
    const { text } = await fetchGitHubFileMeta(state.settings.ghSeedPath);
    catalog = await unwrapSeedJwt(text);
  }

  state.liveLoadedAt = new Date();
  state.dirty = false;
  await loadAuditLogFromServer();
  await loadRobotEventsFromServer();
  return catalog;
}

async function syncCatalogFromGitHubApi() {
  return syncCatalogFromServer();
}

async function putGitHubFileOnce(path, content, message, sha) {
  const apiUrl = githubContentsApiUrl(path);
  const body = {
    message,
    content: toBase64(new TextEncoder().encode(content)),
    branch: state.settings.ghBranch
  };
  if (sha) body.sha = sha;
  return githubRequest(apiUrl, "PUT", body);
}

async function publishGitHubFile(path, content, message, attempt = 0) {
  const apiUrl = githubContentsApiUrl(path);

  let sha = null;
  try {
    const existing = await fetchGitHubFileMeta(path);
    sha = existing.sha;
  } catch (error) {
    if (!String(error.message).includes("HTTP 404")) throw error;
  }

  try {
    return await putGitHubFileOnce(path, content, message, sha);
  } catch (error) {
    const isConflict = String(error.message).includes("HTTP 409");
    if (isConflict && attempt < 8) {
      await new Promise((resolve) => setTimeout(resolve, 600 * (attempt + 1)));
      return publishGitHubFile(path, content, message, attempt + 1);
    }
    if (isConflict) {
      throw new Error(
        "HTTP 409 — plik zmienił się na GitHubie w trakcie zapisu. Kliknij „Odśwież z serwera”, potem ponów publikację."
      );
    }
    throw error;
  }
}

async function githubRequest(url, method, body, options = {}) {
  const s = state.settings;
  const headers = {
    Authorization: `Bearer ${s.ghToken}`,
    Accept: "application/vnd.github+json"
  };
  if (body) headers["Content-Type"] = "application/json";

  let resp;
  try {
    resp = await fetch(url, {
      method,
      headers,
      body: body ? JSON.stringify(body) : undefined
    });
  } catch (error) {
    const hint =
      "Przeglądarka nie połączyła się z api.github.com (sieć, VPN, adblock lub polityka firmowa). " +
      "Sprawdź DevTools → Network, spróbuj innej przeglądarki lub wyłącz blokery.";
    throw new Error(error?.message === "Failed to fetch" ? hint : error.message);
  }

  const text = await resp.text();
  let json = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    json = null;
  }

  if (!resp.ok) {
    const ghMsg = json?.message ?? text?.slice(0, 300) ?? resp.statusText;
    if (resp.status === 401) {
      throw new Error(`HTTP 401 — PAT nieprawidłowy lub wygasł. Wygeneruj nowy token z uprawnieniem repo (Contents: write).`);
    }
    if (resp.status === 403) {
      throw new Error(`HTTP 403 — brak uprawnień: ${ghMsg}. PAT musi mieć dostęp do zapisu w repo ${s.ghOwner}/${s.ghRepo}.`);
    }
    if (resp.status === 404) {
      throw new Error(`HTTP 404 — nie znaleziono: ${ghMsg}. Sprawdź owner/repo/branch (${s.ghBranch}).`);
    }
    if (resp.status === 409) {
      throw new Error(`HTTP 409: ${ghMsg}`);
    }
    throw new Error(`HTTP ${resp.status}: ${ghMsg}`);
  }

  if (options.raw) {
    return json ?? {};
  }

  // GET /contents → sha na poziomie root; PUT → czasem w content.sha
  if (json?.sha) {
    return { sha: json.sha, commit: json.commit?.sha };
  }
  if (json?.content && typeof json.content === "object" && json.content.sha) {
    return { sha: json.content.sha, commit: json.commit?.sha };
  }
  return json ?? {};
}

function appendAudit(action, tokenId, code, notes) {
  auditLog.unshift({
    atUtc: new Date().toISOString(),
    who: operatorWho(),
    action,
    tokenId: tokenId ?? "-",
    code: code ?? "-",
    success: code === "ok" || code === "boot-ok-remote",
    notes: sanitizeAuditNotes(notes ?? "")
  });
  if (auditLog.length > 500) auditLog.length = 500;
  renderAuditTable();
}

function sanitizeAuditNotes(text) {
  return String(text)
    .replace(/ghp_[A-Za-z0-9_]+/g, "ghp_[REDACTED]")
    .replace(/github_pat_[A-Za-z0-9_]+/g, "github_pat_[REDACTED]")
    .replace(/Bearer\s+\S+/gi, "Bearer [REDACTED]")
    .slice(0, 500);
}

function licenseStatus(entry) {
  if (!entry.enabled) return { label: "Odcięta", cls: "bad" };
  const exp = new Date(entry.validToUtc);
  if (Number.isFinite(exp.getTime()) && new Date() > exp) return { label: "Wygasła", cls: "warn" };
  return { label: "Aktywna", cls: "ok" };
}

function licenseActionButtons(entry) {
  const st = licenseStatus(entry);
  const buttons = [];
  if (st.label === "Odcięta" || st.label === "Wygasła") {
    buttons.push({ label: "Odnów", mode: "renew" });
  }
  if (st.label === "Aktywna") {
    buttons.push({ label: "Odetnij", mode: "disable" });
  }
  buttons.push({ label: "Usuń", mode: "delete" });
  return buttons;
}

function updateRobotDownloadButton() {
  const picked = document.querySelectorAll(".license-pick:checked").length;
  byId("btnDownloadRobotPackage").disabled = picked === 0;
}

function robotPackageDefaults() {
  const pkg = state.defaults.robotPackage ?? {};
  return {
    apiUrl: state.defaults.apiBaseUrl ?? state.settings.apiBaseUrl ?? "",
    pepper: state.defaults.pepper ?? state.settings.pepper ?? "",
    nugetUrl: state.defaults.nugetPackageUrl ?? "./assets/nuget/UiPath.System.RoboticSecurity.1.0.7.nupkg",
    dllUrl: state.defaults.nugetDllUrl ?? "./assets/lib/UiPath.System.RoboticSecurity.dll",
    version: state.defaults.nugetVersion ?? "1.0.7",
    graceDays: Number(pkg.graceDays) > 0 ? Number(pkg.graceDays) : 7,
    telemetry: pkg.telemetry !== false,
    killOnDeny: pkg.killOnDeny !== false
  };
}

function initRobotPackageConfigurator() {
  const defaults = robotPackageDefaults();
  const apiEl = byId("robotCfgApiUrl");
  const pepperEl = byId("robotCfgPepper");
  const graceEl = byId("robotCfgGraceDays");
  const telemetryEl = byId("robotCfgTelemetry");
  const killEl = byId("robotCfgKillOnDeny");
  if (!apiEl) return;

  if (!apiEl.dataset.initialized) {
    apiEl.value = defaults.apiUrl;
    pepperEl.value = defaults.pepper;
    graceEl.value = String(defaults.graceDays);
    telemetryEl.checked = defaults.telemetry;
    killEl.checked = defaults.killOnDeny;
    apiEl.dataset.initialized = "1";
  }
}

function readRobotPackageConfig() {
  const defaults = robotPackageDefaults();
  const graceRaw = Number(byId("robotCfgGraceDays")?.value);
  const graceDays = Number.isFinite(graceRaw) && graceRaw > 0 ? Math.min(365, Math.floor(graceRaw)) : defaults.graceDays;

  return {
    apiUrl: byId("robotCfgApiUrl")?.value.trim() || defaults.apiUrl,
    pepper: byId("robotCfgPepper")?.value.trim() || defaults.pepper,
    nugetUrl: defaults.nugetUrl,
    dllUrl: defaults.dllUrl,
    version: defaults.version,
    graceDays,
    telemetry: Boolean(byId("robotCfgTelemetry")?.checked),
    killOnDeny: Boolean(byId("robotCfgKillOnDeny")?.checked)
  };
}

function sanitizeZipSegment(value) {
  return String(value).replace(/[^A-Za-z0-9._-]+/g, "_");
}

function buildRobotPackageTextFiles(entry, cfg) {
  const tokenId = entry.tokenId;
  const owner = entry.owner ?? "";
  const validTo = entry.validToUtc ?? "";
  const nupkgName = `UiPath.System.RoboticSecurity.${cfg.version}.nupkg`;
  const telemetryFlag = cfg.telemetry ? "1" : "0";
  const killFlag = cfg.killOnDeny ? "1" : "0";

  const robotEnv = [
    "# Zmienne srodowiskowe robota UiPath (Panel Windows -> uzytkownik)",
    `OPS_SEED_API_URL=${cfg.apiUrl}`,
    `OPS_SEED_PEPPER=${cfg.pepper}`,
    `OPS_SEED_TELEMETRY=${telemetryFlag}`,
    `OPS_SEED_KILL_ON_DENY=${killFlag}`,
    `OPS_SEED_GRACE_DAYS=${cfg.graceDays}`,
    `FLOW_RUNTIME_TOKEN=${tokenId}`,
    ""
  ].join("\r\n");

  const ustawZmienne = [
    "@echo off",
    "chcp 65001 >nul",
    "echo Ustawianie zmiennych srodowiskowych dla robota UiPath...",
    `setx OPS_SEED_API_URL "${cfg.apiUrl}"`,
    `setx OPS_SEED_PEPPER "${cfg.pepper}"`,
    `setx OPS_SEED_TELEMETRY "${telemetryFlag}"`,
    `setx OPS_SEED_KILL_ON_DENY "${killFlag}"`,
    `setx OPS_SEED_GRACE_DAYS "${cfg.graceDays}"`,
    `setx FLOW_RUNTIME_TOKEN "${tokenId}"`,
    "echo.",
    "echo Gotowe. Zamknij i uruchom ponownie UiPath Studio.",
    "pause"
  ].join("\r\n");

  const instaluj = [
    "@echo off",
    "chcp 65001 >nul",
    "setlocal EnableExtensions",
    "set \"SRC=%~dp0\"",
    "set \"DEST=%USERPROFILE%\\OpsRuntime\"",
    "mkdir \"%DEST%\\lib\" 2>nul",
    "mkdir \"%DEST%\\nuget\" 2>nul",
    "copy /Y \"%SRC%lib\\UiPath.System.RoboticSecurity.dll\" \"%DEST%\\lib\\\" >nul",
    `copy /Y \"%SRC%nuget\\${nupkgName}\" \"%DEST%\\nuget\\\" >nul`,
    "copy /Y \"%SRC%robot.env\" \"%DEST%\\robot.env\" >nul",
    "copy /Y \"%SRC%token.txt\" \"%DEST%\\token.txt\" >nul",
    "echo Zainstalowano do %DEST%",
    "echo Feed NuGet w UiPath: %DEST%\\nuget",
    "pause"
  ].join("\r\n");

  const instrukcja = [
    "Ops Runtime — pakiet robota UiPath",
    "================================",
    "",
    `Token:   ${tokenId}`,
    `Klient:  ${owner}`,
    `Wazna do: ${validTo}`,
    `API:     ${cfg.apiUrl}`,
    `Offline: ${cfg.graceDays} dni od ostatniego potwierdzenia online`,
    "",
    "Tryb online: licencja sprawdzana na zywo — odciecie w panelu dziala natychmiast.",
    "Tryb offline (brak internetu): robot uzywa cache przez OPS_SEED_GRACE_DAYS.",
    "",
    "1. Rozpakuj ZIP i uruchom INSTALUJ.cmd",
    "2. (Opcjonalnie) USTAW-ZMIENNE.cmd — zmienne srodowiskowe Windows",
    "3. UiPath Studio -> Manage Sources -> dodaj folder nuget z paczki",
    "4. Manage Packages -> zainstaluj UiPath.System.RoboticSecurity",
    "5. Invoke Code:",
    "",
    `var token = "${tokenId}";`,
    "if (!UiPath.System.RoboticSecurity.Bootstrapper.TryInitialize(token, out var profile))",
    "    throw new System.Exception(UiPath.System.RoboticSecurity.Bootstrapper.LastCheck.Code);",
    "",
    "Oczekiwany wynik: boot-ok-remote (online) lub boot-ok-cache (offline)"
  ].join("\r\n");

  const invokeCode = [
    `var token = "${tokenId}";`,
    "if (!UiPath.System.RoboticSecurity.Bootstrapper.TryInitialize(token, out var profile))",
    "    throw new System.Exception(UiPath.System.RoboticSecurity.Bootstrapper.LastCheck.Code);",
    "System.Console.WriteLine(\"OK: \" + profile.Owner);"
  ].join("\r\n");

  return {
    nupkgName,
    "token.txt": `${tokenId}\r\n`,
    "robot.env": robotEnv,
    "USTAW-ZMIENNE.cmd": ustawZmienne,
    "INSTALUJ.cmd": instaluj,
    "INSTRUKCJA.txt": instrukcja,
    "invoke-code.txt": invokeCode,
    "config.json": JSON.stringify({
      tokenId,
      owner,
      validToUtc: validTo,
      apiUrl: cfg.apiUrl,
      pepper: cfg.pepper,
      graceDays: cfg.graceDays,
      telemetry: cfg.telemetry,
      killOnDeny: cfg.killOnDeny,
      nugetVersion: cfg.version,
      generatedAt: new Date().toISOString()
    }, null, 2)
  };
}

async function fetchBinary(url) {
  const resp = await fetch(url, { cache: "no-store" });
  if (!resp.ok) {
    throw new Error(`Nie można pobrać ${url} (HTTP ${resp.status}).`);
  }
  return resp.arrayBuffer();
}

async function downloadRobotPackage(tokenId) {
  const entry = catalog.entries.find((e) => e.tokenId === tokenId);
  if (!entry) {
    setStatus("publishStatus", `Nie znaleziono licencji ${tokenId}.`, "bad");
    return;
  }

  if (typeof JSZip === "undefined") {
    setStatus("publishStatus", "Brak biblioteki JSZip — odśwież stronę.", "bad");
    return;
  }

  const cfg = readRobotPackageConfig();
  if (!cfg.apiUrl || !cfg.pepper) {
    setStatus("publishStatus", "Uzupełnij URL API i pepper w konfiguratorze pakietu.", "bad");
    return;
  }

  setStatus("publishStatus", `Przygotowuję pakiet dla ${tokenId}…`, "");

  try {
    const files = buildRobotPackageTextFiles(entry, cfg);
    const [nupkgBuf, dllBuf] = await Promise.all([
      fetchBinary(cfg.nugetUrl),
      fetchBinary(cfg.dllUrl)
    ]);

    const zip = new JSZip();
    const root = zip.folder(`OpsRuntime-${sanitizeZipSegment(tokenId)}`);
    root.file(`nuget/${files.nupkgName}`, nupkgBuf);
    root.file("lib/UiPath.System.RoboticSecurity.dll", dllBuf);
    for (const [name, content] of Object.entries(files)) {
      if (name === "nupkgName") continue;
      root.file(name, content);
    }

    const blob = await zip.generateAsync({ type: "blob", compression: "DEFLATE" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `OpsRuntime-${sanitizeZipSegment(tokenId)}.zip`;
    link.click();
    URL.revokeObjectURL(url);
    setStatus("publishStatus", `Pobrano pakiet UiPath dla ${tokenId}.`, "ok");
  } catch (error) {
    setStatus("publishStatus", error.message, "bad");
  }
}

async function downloadSelectedRobotPackages() {
  const tokens = [...document.querySelectorAll(".license-pick:checked")]
    .map((el) => el.getAttribute("data-token"))
    .filter(Boolean);

  if (!tokens.length) {
    setStatus("publishStatus", "Zaznacz co najmniej jedną licencję.", "warn");
    return;
  }

  if (tokens.length === 1) {
    await downloadRobotPackage(tokens[0]);
    return;
  }

  if (typeof JSZip === "undefined") {
    setStatus("publishStatus", "Brak biblioteki JSZip — odśwież stronę.", "bad");
    return;
  }

  const cfg = readRobotPackageConfig();
  if (!cfg.apiUrl || !cfg.pepper) {
    setStatus("publishStatus", "Uzupełnij URL API i pepper w konfiguratorze pakietu.", "bad");
    return;
  }

  setStatus("publishStatus", `Przygotowuję ${tokens.length} pakietów…`, "");

  try {
    const [nupkgBuf, dllBuf] = await Promise.all([
      fetchBinary(cfg.nugetUrl),
      fetchBinary(cfg.dllUrl)
    ]);
    const zip = new JSZip();

    for (const tokenId of tokens) {
      const entry = catalog.entries.find((e) => e.tokenId === tokenId);
      if (!entry) continue;
      const files = buildRobotPackageTextFiles(entry, cfg);
      const folder = zip.folder(`OpsRuntime-${sanitizeZipSegment(tokenId)}`);
      folder.file(`nuget/${files.nupkgName}`, nupkgBuf);
      folder.file("lib/UiPath.System.RoboticSecurity.dll", dllBuf);
      for (const [name, content] of Object.entries(files)) {
        if (name === "nupkgName") continue;
        folder.file(name, content);
      }
    }

    const blob = await zip.generateAsync({ type: "blob", compression: "DEFLATE" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `OpsRuntime-pakiety-${new Date().toISOString().slice(0, 10)}.zip`;
    link.click();
    URL.revokeObjectURL(url);
    setStatus("publishStatus", `Pobrano ${tokens.length} pakietów UiPath.`, "ok");
  } catch (error) {
    setStatus("publishStatus", error.message, "bad");
  }
}

function renderLicenseTable() {
  const tbody = byId("licenseTableBody");
  tbody.innerHTML = "";
  byId("licenseCount").textContent = String(catalog.entries.length);

  for (const e of catalog.entries) {
    const st = licenseStatus(e);
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td class="col-check"><input type="checkbox" class="license-pick" data-token="${escapeHtml(e.tokenId)}" aria-label="Pobierz pakiet dla ${escapeHtml(e.tokenId)}"></td>
      <td><code>${escapeHtml(e.tokenId)}</code></td>
      <td>${escapeHtml(e.owner ?? "-")}</td>
      <td class="muted">${escapeHtml(e.validToUtc ?? "-")}</td>
      <td><span class="pill ${st.cls}">${st.label}</span></td>
      <td class="row-actions"></td>
    `;
    const actions = tr.querySelector(".row-actions");
    actions.appendChild(actionBtn("Pobierz", () => downloadRobotPackage(e.tokenId)));
    for (const { label, mode } of licenseActionButtons(e)) {
      actions.appendChild(actionBtn(label, () => mutateLicense(e.tokenId, mode)));
    }
    tbody.appendChild(tr);
  }

  tbody.querySelectorAll(".license-pick").forEach((checkbox) => {
    checkbox.addEventListener("change", updateRobotDownloadButton);
  });
  updateRobotDownloadButton();

  if (!catalog.entries.length) {
    const tr = document.createElement("tr");
    tr.innerHTML = `<td colspan="6" class="muted">Brak licencji — utwórz nową lub odśwież z serwera.</td>`;
    tbody.appendChild(tr);
  }
}

function actionBtn(label, onClick) {
  const b = document.createElement("button");
  b.type = "button";
  b.className = label === "Usuń" ? "btn-sm btn-danger" : label === "Pobierz" ? "btn-sm primary" : "btn-sm btn-ghost";
  b.textContent = label;
  b.addEventListener("click", onClick);
  return b;
}

function robotCodeLabel(code) {
  const labels = {
    "boot-ok-remote": "OK (serwer)",
    "boot-ok-cache": "OK (cache)",
    "boot-0x11": "Nieznany token",
    "boot-0x12": "Odcięta",
    "boot-0x14": "Wygasła",
    "boot-0x15": "Zła maszyna",
    "boot-0x16": "Zły podpis",
    "boot-0xFF": "Błąd ogólny"
  };
  return labels[code] ?? code;
}

function filterRobotEvents() {
  const tokenQ = byId("robotFilterToken").value.trim().toLowerCase();
  const codeQ = byId("robotFilterCode").value.trim().toLowerCase();
  const resultQ = byId("robotFilterResult").value;

  return robotEvents.filter((row) => {
    if (tokenQ && !String(row.tokenId ?? "").toLowerCase().includes(tokenQ)) return false;
    if (codeQ && !String(row.code ?? "").toLowerCase().includes(codeQ)) return false;
    if (resultQ === "ok" && !row.success) return false;
    if (resultQ === "fail" && row.success) return false;
    return true;
  });
}

function renderRobotEventsStats() {
  const el = byId("robotEventsStats");
  if (!el) return;

  const now = Date.now();
  const dayAgo = now - 24 * 60 * 60 * 1000;
  const last24h = robotEvents.filter((e) => {
    const t = Date.parse(e.atUtc ?? "");
    return Number.isFinite(t) && t >= dayAgo;
  });
  const okCount = robotEvents.filter((e) => e.success).length;
  const failCount = robotEvents.length - okCount;
  const cacheCount = robotEvents.filter((e) => e.usedCache).length;

  el.innerHTML = `
    <div class="stat-chip"><span>Ostatnie 24h</span><strong>${last24h.length}</strong></div>
    <div class="stat-chip ok"><span>Udane</span><strong>${okCount}</strong></div>
    <div class="stat-chip bad"><span>Odrzucone</span><strong>${failCount}</strong></div>
    <div class="stat-chip"><span>Z cache</span><strong>${cacheCount}</strong></div>
  `;
}

function renderRobotEventsTable() {
  const tbody = byId("robotEventsTableBody");
  const countEl = byId("robotEventsCount");
  if (!tbody) return;

  const rows = filterRobotEvents().slice(0, 150);
  if (countEl) countEl.textContent = String(robotEvents.length);

  tbody.innerHTML = "";
  for (const row of rows) {
    const tr = document.createElement("tr");
    const resultCls = row.success ? "ok" : "bad";
    const resultLabel = row.success ? "OK" : "Błąd";
    tr.innerHTML = `
      <td>${escapeHtml(formatUtcShort(row.atUtc))}</td>
      <td><code>${escapeHtml(row.tokenId ?? "-")}</code></td>
      <td>${escapeHtml(row.machine ?? "-")}</td>
      <td><span class="pill ${resultCls}" title="${escapeHtml(row.code ?? "")}">${escapeHtml(robotCodeLabel(row.code))}</span></td>
      <td><span class="pill ${resultCls}">${resultLabel}</span></td>
      <td>${row.usedCache ? "tak" : "nie"}</td>
      <td>${escapeHtml(row.processName ?? "-")}</td>
      <td>${escapeHtml(row.windowsIdentity ?? "-")}</td>
      <td>${escapeHtml(row.notes ?? "")}</td>
    `;
    tbody.appendChild(tr);
  }

  if (!rows.length) {
    tbody.innerHTML = `<tr><td colspan="9" class="muted">Brak zdarzeń — włącz telemetrię na robotach (OPS_SEED_TELEMETRY=1).</td></tr>`;
  }

  renderRobotEventsStats();
}

function formatUtcShort(value) {
  if (!value) return "-";
  const d = new Date(value);
  if (!Number.isFinite(d.getTime())) return String(value);
  return d.toISOString().replace("T", " ").replace(/\.\d{3}Z$/, " UTC");
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
  renderRobotEventsTable();
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
  const sep = url.includes("?") ? "&" : "?";
  const resp = await fetch(`${url}${sep}_=${Date.now()}`, { cache: "no-store" });
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

async function hmacSha256RawKeyBase64Url(rawKeyBytes, message) {
  const key = await crypto.subtle.importKey(
    "raw",
    rawKeyBytes,
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"]
  );
  const sig = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(message));
  return toBase64Url(new Uint8Array(sig));
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
