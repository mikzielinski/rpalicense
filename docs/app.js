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
  const now = new Date();
  const plusYear = new Date(now.getTime() + 365 * 24 * 60 * 60 * 1000);
  byId("simNow").value = toLocalInputValue(now);
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

  byId("formatCatalog").addEventListener("click", () => {
    const parsed = tryParseCatalog();
    if (!parsed.ok) {
      setStatus("catalogStatus", parsed.error, "bad");
      return;
    }

    byId("catalogInput").value = JSON.stringify(parsed.catalog, null, 2);
    setStatus("catalogStatus", "Sformatowano catalog.json", "ok");
    renderEntries(parsed.catalog);
  });

  byId("validateCatalog").addEventListener("click", validateAndRenderCatalog);

  byId("downloadCatalog").addEventListener("click", () => {
    const parsed = tryParseCatalog();
    if (!parsed.ok) {
      setStatus("catalogStatus", parsed.error, "bad");
      return;
    }
    downloadText("catalog.json", JSON.stringify(parsed.catalog, null, 2));
  });

  byId("simulateCheck").addEventListener("click", simulateRuntimeCheck);
  byId("buildJwt").addEventListener("click", buildJwtFromCatalog);
  byId("inspectJwt").addEventListener("click", inspectJwt);
  byId("downloadJwt").addEventListener("click", () => {
    const token = byId("jwtInput").value.trim();
    if (!token) {
      setPre("jwtResult", "Brak seed.jwt do pobrania.");
      return;
    }
    downloadText("seed.jwt", token + "\n");
  });

  byId("probeRemote").addEventListener("click", probeRemote);
  byId("disableToken").addEventListener("click", disableToken);
  byId("renewToken").addEventListener("click", renewToken);
  byId("resealEntries").addEventListener("click", resealEntries);
  byId("publishJwt").addEventListener("click", publishJwtToGitHub);
}

function validateAndRenderCatalog() {
  const parsed = tryParseCatalog();
  if (!parsed.ok) {
    setStatus("catalogStatus", parsed.error, "bad");
    byId("entriesSummary").textContent = "";
    byId("entriesTableBody").innerHTML = "";
    return;
  }

  const errors = validateCatalog(parsed.catalog);
  if (errors.length > 0) {
    setStatus("catalogStatus", `Walidacja: ${errors.length} problem(y).`, "warn");
    byId("entriesSummary").textContent = errors.join(" | ");
  } else {
    setStatus("catalogStatus", "Katalog poprawny.", "ok");
    byId("entriesSummary").textContent = `Wpisow: ${parsed.catalog.entries.length}`;
  }
  renderEntries(parsed.catalog);
}

function tryParseCatalog() {
  const raw = byId("catalogInput").value.trim();
  if (!raw) {
    return { ok: false, error: "Pusty catalog.json" };
  }

  try {
    const parsed = JSON.parse(raw);
    if (!parsed || !Array.isArray(parsed.entries)) {
      return { ok: false, error: "Brak pola entries[] w catalog.json" };
    }
    return { ok: true, catalog: parsed };
  } catch (error) {
    return { ok: false, error: `Niepoprawny JSON: ${error.message}` };
  }
}

function validateCatalog(catalog) {
  const errors = [];
  for (let i = 0; i < catalog.entries.length; i += 1) {
    const e = catalog.entries[i];
    const prefix = `entries[${i}]`;

    if (!e || typeof e !== "object") {
      errors.push(`${prefix}: nie jest obiektem`);
      continue;
    }

    ["tokenId", "owner", "validToUtc", "blob", "nonce", "tag", "seal"].forEach((field) => {
      if (!e[field] || typeof e[field] !== "string") {
        errors.push(`${prefix}.${field}: wymagany string`);
      }
    });

    if (typeof e.enabled !== "boolean") {
      errors.push(`${prefix}.enabled: wymagane true/false`);
    }

    if (!Array.isArray(e.hosts)) {
      errors.push(`${prefix}.hosts: wymagana tablica`);
    }

    if (e.validToUtc && Number.isNaN(Date.parse(e.validToUtc))) {
      errors.push(`${prefix}.validToUtc: niepoprawna data UTC`);
    }
  }

  return errors;
}

function renderEntries(catalog) {
  const tbody = byId("entriesTableBody");
  tbody.innerHTML = "";
  const now = new Date();

  catalog.entries.forEach((entry) => {
    const tr = document.createElement("tr");
    const validTo = new Date(entry.validToUtc);
    let status = "active";
    if (!entry.enabled) {
      status = "disabled";
    } else if (Number.isFinite(validTo.getTime()) && now > validTo) {
      status = "expired";
    }

    tr.innerHTML = `
      <td>${safe(entry.tokenId)}</td>
      <td>${safe(entry.owner)}</td>
      <td>${safe(entry.validToUtc)}</td>
      <td>${entry.enabled ? "true" : "false"}</td>
      <td>${safe((entry.hosts || []).join(", "))}</td>
      <td>${status}</td>
    `;
    tbody.appendChild(tr);
  });
}

function simulateRuntimeCheck() {
  const parsed = tryParseCatalog();
  if (!parsed.ok) {
    setPre("simResult", parsed.error);
    return;
  }

  const tokenId = byId("simTokenId").value.trim();
  const machine = byId("simMachine").value.trim().toUpperCase();
  const nowInput = byId("simNow").value;
  const now = nowInput ? new Date(nowInput) : new Date();

  if (!tokenId) {
    setPre("simResult", "Podaj tokenId.");
    return;
  }

  const entry = parsed.catalog.entries.find((x) => x.tokenId === tokenId);
  if (!entry) {
    setPre("simResult", JSON.stringify({
      success: false,
      code: "boot-0x11",
      notes: "token-not-found"
    }, null, 2));
    return;
  }

  if (!entry.enabled) {
    setPre("simResult", JSON.stringify({
      success: false,
      code: "boot-0x12",
      notes: "disabled-by-remote-switch"
    }, null, 2));
    return;
  }

  const validTo = new Date(entry.validToUtc);
  if (Number.isFinite(validTo.getTime()) && now > validTo) {
    setPre("simResult", JSON.stringify({
      success: false,
      code: "boot-0x14",
      notes: "expired"
    }, null, 2));
    return;
  }

  const hosts = Array.isArray(entry.hosts) ? entry.hosts.map((h) => String(h).trim().toUpperCase()).filter(Boolean) : [];
  if (hosts.length > 0 && machine && !hosts.includes(machine)) {
    setPre("simResult", JSON.stringify({
      success: false,
      code: "boot-0x15",
      notes: `machine-mismatch expected:${hosts.join(",")}`
    }, null, 2));
    return;
  }

  setPre("simResult", JSON.stringify({
    success: true,
    code: "boot-ok-remote",
    tokenId: entry.tokenId,
    owner: entry.owner,
    validToUtc: entry.validToUtc
  }, null, 2));
}

async function buildJwtFromCatalog() {
  const parsed = tryParseCatalog();
  if (!parsed.ok) {
    setPre("jwtResult", parsed.error);
    return;
  }

  const issuer = byId("jwtIssuer").value.trim();
  const audience = byId("jwtAudience").value.trim();
  const expLocal = byId("jwtExpUtc").value;
  const signingKey = byId("jwtSigningKey").value;
  const envelopePepper = byId("jwtEnvelopePepper").value;

  if (!issuer || !audience || !expLocal || !signingKey || !envelopePepper) {
    setPre("jwtResult", "Uzupelnij issuer/audience/exp/signingKey/envelopePepper.");
    return;
  }

  try {
    const catalogJson = JSON.stringify(parsed.catalog);
    const envelopeKey = await deriveSha256Key(`${audience}:${envelopePepper}`);
    const enc = await encryptAesGcm(envelopeKey, catalogJson);
    const expUnix = Math.floor(new Date(expLocal).getTime() / 1000);
    const nowUnix = Math.floor(Date.now() / 1000);

    const header = { alg: "HS256", typ: "JWT" };
    const payload = {
      iss: issuer,
      aud: audience,
      exp: expUnix,
      nbf: nowUnix - 30,
      blob: toBase64(enc.ciphertext),
      nonce: toBase64(enc.nonce),
      tag: toBase64(enc.tag)
    };

    const headerPart = toBase64UrlUtf8(JSON.stringify(header));
    const payloadPart = toBase64UrlUtf8(JSON.stringify(payload));
    const signed = `${headerPart}.${payloadPart}`;
    const sig = await hmacSha256Base64Url(signingKey, signed);
    const jwt = `${signed}.${sig}`;

    byId("jwtInput").value = jwt;
    setPre("jwtResult", JSON.stringify({
      success: true,
      code: "jwt-built",
      iss: issuer,
      aud: audience,
      exp: expUnix
    }, null, 2));
  } catch (error) {
    setPre("jwtResult", `Blad generowania JWT: ${error.message}`);
  }
}

async function inspectJwt() {
  const jwt = byId("jwtInput").value.trim();
  const signingKey = byId("jwtSigningKey").value;
  const envelopePepper = byId("jwtEnvelopePepper").value;
  if (!jwt) {
    setPre("jwtResult", "Brak seed.jwt.");
    return;
  }

  try {
    const parts = jwt.split(".");
    if (parts.length !== 3) {
      throw new Error("JWT musi miec 3 czesci.");
    }

    const header = JSON.parse(fromBase64UrlUtf8(parts[0]));
    const claims = JSON.parse(fromBase64UrlUtf8(parts[1]));
    const signed = `${parts[0]}.${parts[1]}`;

    let signatureValid = null;
    if (signingKey) {
      const expected = await hmacSha256Base64Url(signingKey, signed);
      signatureValid = safeEqual(expected, parts[2]);
    }

    let decryptedCatalogInfo = null;
    if (envelopePepper && claims.aud && claims.blob && claims.nonce && claims.tag) {
      const key = await deriveSha256Key(`${claims.aud}:${envelopePepper}`);
      const plain = await decryptAesGcm(key, fromBase64(claims.nonce), fromBase64(claims.tag), fromBase64(claims.blob));
      const catalog = JSON.parse(plain);
      decryptedCatalogInfo = {
        entries: Array.isArray(catalog.entries) ? catalog.entries.length : 0
      };
    }

    setPre("jwtResult", JSON.stringify({
      header,
      claims,
      signatureValid,
      decryptedCatalogInfo
    }, null, 2));
  } catch (error) {
    setPre("jwtResult", `Blad analizy JWT: ${error.message}`);
  }
}

async function probeRemote() {
  const url = byId("remoteUrl").value.trim();
  const bearer = byId("remoteBearer").value.trim();
  if (!url) {
    setPre("remoteResult", "Podaj URL.");
    return;
  }

  const headers = {};
  if (bearer) {
    headers.Authorization = `Bearer ${bearer}`;
  }

  try {
    const start = performance.now();
    const response = await fetch(url, { headers });
    const elapsed = Math.round(performance.now() - start);
    const body = await response.text();

    const out = {
      status: response.status,
      ok: response.ok,
      elapsedMs: elapsed,
      bytes: body.length
    };

    if (response.ok) {
      const maybeJwt = body.trim();
      const parts = maybeJwt.split(".");
      if (parts.length === 3) {
        try {
          const claims = JSON.parse(fromBase64UrlUtf8(parts[1]));
          out.jwt = {
            iss: claims.iss ?? null,
            aud: claims.aud ?? null,
            exp: claims.exp ?? null,
            nbf: claims.nbf ?? null
          };
          byId("jwtInput").value = maybeJwt;
        } catch {
          out.jwt = "token-parse-failed";
        }
      } else {
        out.jwt = "response-is-not-jwt";
      }
    }

    setPre("remoteResult", JSON.stringify(out, null, 2));
  } catch (error) {
    setPre("remoteResult", `Blad pobrania: ${error.message}`);
  }
}

function disableToken() {
  mutateToken((entry, tokenId) => {
    entry.enabled = false;
    return `Token ${tokenId} ustawiony na enabled=false`;
  });
}

function renewToken() {
  const validToLocal = byId("opValidToUtc").value;
  if (!validToLocal) {
    setStatus("catalogStatus", "Podaj nowe validToUtc.", "warn");
    return;
  }

  const validToUtc = new Date(validToLocal).toISOString();
  mutateToken((entry, tokenId) => {
    entry.enabled = true;
    entry.validToUtc = validToUtc;
    return `Token ${tokenId} odnowiony do ${validToUtc}`;
  });
}

function mutateToken(mutator) {
  const parsed = tryParseCatalog();
  if (!parsed.ok) {
    setStatus("catalogStatus", parsed.error, "bad");
    return;
  }

  const tokenId = byId("opTokenId").value.trim();
  if (!tokenId) {
    setStatus("catalogStatus", "Podaj tokenId do operacji.", "warn");
    return;
  }

  const entry = parsed.catalog.entries.find((x) => x.tokenId === tokenId);
  if (!entry) {
    setStatus("catalogStatus", `Nie znaleziono tokenId: ${tokenId}`, "bad");
    return;
  }

  const message = mutator(entry, tokenId);
  byId("catalogInput").value = JSON.stringify(parsed.catalog, null, 2);
  validateAndRenderCatalog();
  setStatus("catalogStatus", `${message}. Przelicz pole seal i wygeneruj nowy seed.jwt.`, "warn");
}

async function resealEntries() {
  const parsed = tryParseCatalog();
  if (!parsed.ok) {
    setStatus("catalogStatus", parsed.error, "bad");
    return;
  }

  const jwkText = byId("sealJwk").value.trim();
  if (!jwkText) {
    setStatus("catalogStatus", "Wklej prywatny klucz JWK (RSA).", "warn");
    return;
  }

  try {
    const jwk = JSON.parse(jwkText);
    const key = await crypto.subtle.importKey(
      "jwk",
      jwk,
      { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
      false,
      ["sign"]
    );

    for (const entry of parsed.catalog.entries) {
      const canonical = canonicalizeEntry(entry);
      const signature = await crypto.subtle.sign(
        "RSASSA-PKCS1-v1_5",
        key,
        new TextEncoder().encode(canonical)
      );
      entry.seal = toBase64(new Uint8Array(signature));
    }

    byId("catalogInput").value = JSON.stringify(parsed.catalog, null, 2);
    validateAndRenderCatalog();
    setStatus("catalogStatus", "Przeliczono seal dla wszystkich wpisow.", "ok");
  } catch (error) {
    setStatus("catalogStatus", `Blad podpisu seal: ${error.message}`, "bad");
  }
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
    setPre("ghResult", "Uzupelnij owner/repo/path/token i wygeneruj seed.jwt.");
    return;
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
      throw new Error(`GET contents failed: HTTP ${getResp.status}`);
    }

    const payload = {
      message,
      content: toBase64(new TextEncoder().encode(`${jwt}\n`)),
      branch
    };
    if (sha) {
      payload.sha = sha;
    }

    const putResp = await fetch(apiUrl, {
      method: "PUT",
      headers,
      body: JSON.stringify(payload)
    });

    if (!putResp.ok) {
      const body = await putResp.text();
      throw new Error(`PUT failed: HTTP ${putResp.status} ${body}`);
    }

    const putJson = await putResp.json();
    setPre("ghResult", JSON.stringify({
      success: true,
      commit: putJson.commit?.sha ?? null,
      file: putJson.content?.path ?? path,
      branch
    }, null, 2));
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
  if (cls) {
    el.classList.add(cls);
  }
}

function setPre(id, text) {
  byId(id).textContent = text;
}

function safe(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
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
  const bytes = new TextEncoder().encode(input);
  const hash = await crypto.subtle.digest("SHA-256", bytes);
  return new Uint8Array(hash);
}

async function encryptAesGcm(rawKeyBytes, plaintext) {
  const key = await crypto.subtle.importKey("raw", rawKeyBytes, { name: "AES-GCM" }, false, ["encrypt"]);
  const nonce = crypto.getRandomValues(new Uint8Array(12));
  const plain = new TextEncoder().encode(plaintext);
  const combined = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv: nonce }, key, plain));
  const tag = combined.slice(combined.length - 16);
  const ciphertext = combined.slice(0, combined.length - 16);
  return { ciphertext, nonce, tag };
}

async function decryptAesGcm(rawKeyBytes, nonce, tag, ciphertext) {
  const key = await crypto.subtle.importKey("raw", rawKeyBytes, { name: "AES-GCM" }, false, ["decrypt"]);
  const combined = concatBytes(ciphertext, tag);
  const plain = await crypto.subtle.decrypt({ name: "AES-GCM", iv: nonce }, key, combined);
  return new TextDecoder().decode(new Uint8Array(plain));
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

function toBase64(bytes) {
  let binary = "";
  for (let i = 0; i < bytes.length; i += 1) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

function fromBase64(text) {
  const binary = atob(text);
  const out = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) {
    out[i] = binary.charCodeAt(i);
  }
  return out;
}

function toBase64Url(bytes) {
  return toBase64(bytes).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/g, "");
}

function toBase64UrlUtf8(text) {
  return toBase64Url(new TextEncoder().encode(text));
}

function fromBase64UrlUtf8(text) {
  const padded = text.replaceAll("-", "+").replaceAll("_", "/") + "=".repeat((4 - (text.length % 4)) % 4);
  return new TextDecoder().decode(fromBase64(padded));
}

function concatBytes(a, b) {
  const out = new Uint8Array(a.length + b.length);
  out.set(a, 0);
  out.set(b, a.length);
  return out;
}

function safeEqual(a, b) {
  if (a.length !== b.length) {
    return false;
  }
  let diff = 0;
  for (let i = 0; i < a.length; i += 1) {
    diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  }
  return diff === 0;
}
