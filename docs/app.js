const byId = (id) => document.getElementById(id);

const sampleCatalog = {
  entries: [
    {
      tokenId: "RT-2026-CLIENT-001",
      owner: "Klient Sp. z o.o.",
      validToUtc: "2026-12-31T23:59:59Z",
      enabled: true,
      hosts: ["ROBOT01", "ROBOT02"],
      blob: "BASE64_CIPHERTEXT",
      nonce: "BASE64_NONCE",
      tag: "BASE64_TAG",
      seal: "BASE64_RSA_SIGNATURE"
    }
  ]
};

init();

function init() {
  const plusYear = new Date(Date.now() + 365 * 24 * 60 * 60 * 1000);
  byId("jwtExpUtc").value = toLocalInputValue(plusYear);
  byId("opValidToUtc").value = toLocalInputValue(plusYear);
  byId("catalogInput").value = JSON.stringify(sampleCatalog, null, 2);

  bindActions();
  validateAndRenderCatalog();
}

function bindActions() {
  byId("loadSampleCatalog").addEventListener("click", () => {
    byId("catalogInput").value = JSON.stringify(sampleCatalog, null, 2);
    validateAndRenderCatalog();
  });
  byId("validateCatalog").addEventListener("click", validateAndRenderCatalog);
  byId("downloadCatalog").addEventListener("click", downloadCatalog);
  byId("disableToken").addEventListener("click", () => mutateSelectedToken("disable"));
  byId("renewToken").addEventListener("click", () => mutateSelectedToken("renew"));
  byId("resealEntries").addEventListener("click", resealEntries);
  byId("buildJwt").addEventListener("click", buildJwtFromCatalog);
  byId("downloadJwt").addEventListener("click", downloadJwt);
  byId("simulateCheck").addEventListener("click", simulateRuntimeCheck);
  byId("publishJwt").addEventListener("click", publishJwtToGitHub);
}

function tryParseCatalog() {
  const raw = byId("catalogInput").value.trim();
  if (!raw) {
    return { ok: false, error: "Pusty catalog.json" };
  }
  try {
    const catalog = JSON.parse(raw);
    if (!catalog || !Array.isArray(catalog.entries)) {
      return { ok: false, error: "Brak entries[] w catalog.json" };
    }
    return { ok: true, catalog };
  } catch (error) {
    return { ok: false, error: `JSON error: ${error.message}` };
  }
}

function validateCatalog(catalog) {
  const errors = [];
  for (const [i, e] of catalog.entries.entries()) {
    if (!e?.tokenId) errors.push(`entries[${i}].tokenId`);
    if (!e?.validToUtc || Number.isNaN(Date.parse(e.validToUtc))) errors.push(`entries[${i}].validToUtc`);
    if (typeof e?.enabled !== "boolean") errors.push(`entries[${i}].enabled`);
    if (!Array.isArray(e?.hosts)) errors.push(`entries[${i}].hosts`);
    if (!e?.blob || !e?.nonce || !e?.tag || !e?.seal) errors.push(`entries[${i}].blob/nonce/tag/seal`);
  }
  return errors;
}

function validateAndRenderCatalog() {
  const parsed = tryParseCatalog();
  if (!parsed.ok) {
    setStatus("catalogStatus", parsed.error, "bad");
    byId("catalogSummary").textContent = "";
    fillTokenSelect([]);
    return;
  }

  const errors = validateCatalog(parsed.catalog);
  if (errors.length > 0) {
    setStatus("catalogStatus", `Katalog ma bledy: ${errors.join(", ")}`, "warn");
  } else {
    setStatus("catalogStatus", `Katalog OK. Wpisow: ${parsed.catalog.entries.length}`, "ok");
  }

  fillTokenSelect(parsed.catalog.entries);
  byId("catalogSummary").textContent = buildCatalogSummary(parsed.catalog.entries);
}

function buildCatalogSummary(entries) {
  const now = new Date();
  const lines = entries.map((e) => {
    const exp = new Date(e.validToUtc);
    let status = "active";
    if (!e.enabled) status = "disabled";
    else if (Number.isFinite(exp.getTime()) && now > exp) status = "expired";
    return `${e.tokenId} | ${e.owner ?? "-"} | ${e.validToUtc} | ${status}`;
  });
  return lines.join("\n");
}

function fillTokenSelect(entries) {
  const select = byId("tokenSelect");
  const current = select.value;
  select.innerHTML = "";
  for (const e of entries) {
    const opt = document.createElement("option");
    opt.value = e.tokenId;
    opt.textContent = `${e.tokenId} (${e.owner ?? "-"})`;
    select.appendChild(opt);
  }
  if (current && [...select.options].some((o) => o.value === current)) {
    select.value = current;
  }
}

function downloadCatalog() {
  const parsed = tryParseCatalog();
  if (!parsed.ok) return setStatus("catalogStatus", parsed.error, "bad");
  downloadText("catalog.json", JSON.stringify(parsed.catalog, null, 2));
}

function mutateSelectedToken(mode) {
  const parsed = tryParseCatalog();
  if (!parsed.ok) return setStatus("catalogStatus", parsed.error, "bad");
  const tokenId = byId("tokenSelect").value;
  const entry = parsed.catalog.entries.find((x) => x.tokenId === tokenId);
  if (!entry) return setStatus("catalogStatus", "Wybierz tokenId.", "warn");

  if (mode === "disable") {
    entry.enabled = false;
    setStatus("catalogStatus", `Token ${tokenId} odciety. Przelicz seal + seed.jwt.`, "warn");
  } else {
    const dt = byId("opValidToUtc").value;
    if (!dt) return setStatus("catalogStatus", "Podaj nowe validToUtc.", "warn");
    entry.enabled = true;
    entry.validToUtc = new Date(dt).toISOString();
    setStatus("catalogStatus", `Token ${tokenId} odnowiony. Przelicz seal + seed.jwt.`, "warn");
  }

  byId("catalogInput").value = JSON.stringify(parsed.catalog, null, 2);
  validateAndRenderCatalog();
}

async function resealEntries() {
  const parsed = tryParseCatalog();
  if (!parsed.ok) return setStatus("catalogStatus", parsed.error, "bad");
  const jwkText = byId("sealJwk").value.trim();
  if (!jwkText) return setStatus("catalogStatus", "Wklej prywatny klucz JWK.", "warn");

  try {
    const jwk = JSON.parse(jwkText);
    const key = await crypto.subtle.importKey(
      "jwk",
      jwk,
      { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
      false,
      ["sign"]
    );
    for (const e of parsed.catalog.entries) {
      const canonical = canonicalizeEntry(e);
      const sig = await crypto.subtle.sign(
        "RSASSA-PKCS1-v1_5",
        key,
        new TextEncoder().encode(canonical)
      );
      e.seal = toBase64(new Uint8Array(sig));
    }
    byId("catalogInput").value = JSON.stringify(parsed.catalog, null, 2);
    validateAndRenderCatalog();
    setStatus("catalogStatus", "Seal przeliczone.", "ok");
  } catch (error) {
    setStatus("catalogStatus", `Blad seal: ${error.message}`, "bad");
  }
}

async function buildJwtFromCatalog() {
  const parsed = tryParseCatalog();
  if (!parsed.ok) return setPre("jwtResult", parsed.error);

  const issuer = byId("jwtIssuer").value.trim();
  const audience = byId("jwtAudience").value.trim();
  const expLocal = byId("jwtExpUtc").value;
  const signingKey = byId("jwtSigningKey").value;
  const envelopePepper = byId("jwtEnvelopePepper").value;
  if (!issuer || !audience || !expLocal || !signingKey || !envelopePepper) {
    return setPre("jwtResult", "Uzupelnij issuer/audience/exp/signingKey/envelopePepper.");
  }

  try {
    const envelopeKey = await deriveSha256Key(`${audience}:${envelopePepper}`);
    const enc = await encryptAesGcm(envelopeKey, JSON.stringify(parsed.catalog));
    const header = { alg: "HS256", typ: "JWT" };
    const payload = {
      iss: issuer,
      aud: audience,
      exp: Math.floor(new Date(expLocal).getTime() / 1000),
      nbf: Math.floor(Date.now() / 1000) - 30,
      blob: toBase64(enc.ciphertext),
      nonce: toBase64(enc.nonce),
      tag: toBase64(enc.tag)
    };
    const headerPart = toBase64UrlUtf8(JSON.stringify(header));
    const payloadPart = toBase64UrlUtf8(JSON.stringify(payload));
    const signed = `${headerPart}.${payloadPart}`;
    const sig = await hmacSha256Base64Url(signingKey, signed);
    byId("jwtInput").value = `${signed}.${sig}`;
    setPre("jwtResult", "seed.jwt wygenerowany.");
  } catch (error) {
    setPre("jwtResult", `Blad JWT: ${error.message}`);
  }
}

function downloadJwt() {
  const jwt = byId("jwtInput").value.trim();
  if (!jwt) return setPre("jwtResult", "Najpierw wygeneruj seed.jwt.");
  downloadText("seed.jwt", `${jwt}\n`);
}

function simulateRuntimeCheck() {
  const parsed = tryParseCatalog();
  if (!parsed.ok) return setPre("simResult", parsed.error);
  const tokenId = byId("simTokenId").value.trim();
  const machine = byId("simMachine").value.trim().toUpperCase();
  const entry = parsed.catalog.entries.find((x) => x.tokenId === tokenId);
  if (!entry) return setPre("simResult", '{"success":false,"code":"boot-0x11"}');
  if (!entry.enabled) return setPre("simResult", '{"success":false,"code":"boot-0x12"}');
  if (new Date() > new Date(entry.validToUtc)) return setPre("simResult", '{"success":false,"code":"boot-0x14"}');
  const hosts = Array.isArray(entry.hosts) ? entry.hosts.map((h) => String(h).toUpperCase()) : [];
  if (hosts.length > 0 && machine && !hosts.includes(machine)) {
    return setPre("simResult", '{"success":false,"code":"boot-0x15"}');
  }
  return setPre("simResult", '{"success":true,"code":"boot-ok-remote"}');
}

async function publishJwtToGitHub() {
  const owner = byId("ghOwner").value.trim();
  const repo = byId("ghRepo").value.trim();
  const branch = byId("ghBranch").value.trim() || "main";
  const path = byId("ghPath").value.trim();
  const token = byId("ghToken").value.trim();
  const message = byId("ghMessage").value.trim() || "Update seed.jwt";
  const jwt = byId("jwtInput").value.trim();

  if (!owner || !repo || !path || !token || !jwt) {
    return setPre("ghResult", "Uzupelnij owner/repo/path/token i seed.jwt.");
  }

  const apiUrl = `https://api.github.com/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}/contents/${path}`;
  const headers = {
    Authorization: `Bearer ${token}`,
    Accept: "application/vnd.github+json",
    "Content-Type": "application/json"
  };

  try {
    let sha = null;
    const getResp = await fetch(`${apiUrl}?ref=${encodeURIComponent(branch)}`, { headers });
    if (getResp.status === 200) {
      const getJson = await getResp.json();
      sha = getJson.sha ?? null;
    } else if (getResp.status !== 404) {
      throw new Error(`GET failed HTTP ${getResp.status}`);
    }

    const body = {
      message,
      content: toBase64(new TextEncoder().encode(`${jwt}\n`)),
      branch
    };
    if (sha) body.sha = sha;

    const putResp = await fetch(apiUrl, { method: "PUT", headers, body: JSON.stringify(body) });
    if (!putResp.ok) {
      const txt = await putResp.text();
      throw new Error(`PUT failed HTTP ${putResp.status} ${txt}`);
    }
    const putJson = await putResp.json();
    setPre("ghResult", `OK commit: ${putJson.commit?.sha ?? "-"}`);
  } catch (error) {
    setPre("ghResult", `Blad publikacji: ${error.message}`);
  }
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

function setStatus(id, text, cls) {
  const el = byId(id);
  el.textContent = text;
  el.classList.remove("ok", "warn", "bad");
  if (cls) el.classList.add(cls);
}

function setPre(id, text) {
  byId(id).textContent = text;
}

function toLocalInputValue(date) {
  const pad = (x) => String(x).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function downloadText(filename, text) {
  const blob = new Blob([text], { type: "text/plain;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

async function deriveSha256Key(input) {
  const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(input));
  return new Uint8Array(hash);
}

async function encryptAesGcm(rawKeyBytes, plaintext) {
  const key = await crypto.subtle.importKey("raw", rawKeyBytes, { name: "AES-GCM" }, false, ["encrypt"]);
  const nonce = crypto.getRandomValues(new Uint8Array(12));
  const plain = new TextEncoder().encode(plaintext);
  const combined = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv: nonce }, key, plain));
  return {
    ciphertext: combined.slice(0, combined.length - 16),
    nonce,
    tag: combined.slice(combined.length - 16)
  };
}

async function hmacSha256Base64Url(secret, message) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const sig = await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(message));
  return toBase64Url(new Uint8Array(sig));
}

function toBase64(bytes) {
  let b = "";
  for (let i = 0; i < bytes.length; i += 1) b += String.fromCharCode(bytes[i]);
  return btoa(b);
}

function toBase64Url(bytes) {
  return toBase64(bytes).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/g, "");
}

function toBase64UrlUtf8(text) {
  return toBase64Url(new TextEncoder().encode(text));
}
