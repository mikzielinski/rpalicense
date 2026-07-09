import fs from "node:fs";
import path from "node:path";

const eventType = process.env.EVENT_TYPE ?? "";
const payload = JSON.parse(process.env.CLIENT_PAYLOAD || "{}");
const operatorKey = process.env.OPS_API_OPERATOR_KEY ?? "";
const robotKey = process.env.OPS_API_ROBOT_KEY ?? "";

const SEED_PATH = "docs/assets/seed.jwt";
const AUDIT_PATH = "docs/assets/audit-log.json";
const EVENTS_PATH = "docs/assets/robot-events.json";
const MAX_ENTRIES = 500;

function fail(message) {
  console.error(message);
  process.exit(1);
}

function requireKey(expected, provided, label) {
  if (!expected || !provided || expected !== provided) {
    fail(`Invalid ${label} API key.`);
  }
}

function readJson(filePath, fallback) {
  if (!fs.existsSync(filePath)) {
    return fallback;
  }
  return JSON.parse(fs.readFileSync(filePath, "utf8"));
}

function writeJson(filePath, value) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, `${JSON.stringify(value, null, 2)}\n`, "utf8");
}

function writeText(filePath, text) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, text.endsWith("\n") ? text : `${text}\n`, "utf8");
}

switch (eventType) {
  case "publish-seed": {
    requireKey(operatorKey, payload.apiKey, "operator");
    const jwt = String(payload.jwt ?? "").trim();
    if (!jwt.startsWith("eyJ")) {
      fail("publish-seed: invalid jwt payload.");
    }
    writeText(SEED_PATH, jwt);
    console.log(`Updated ${SEED_PATH}`);
    break;
  }

  case "publish-audit": {
    requireKey(operatorKey, payload.apiKey, "operator");
    const entries = Array.isArray(payload.entries) ? payload.entries : [];
    writeJson(AUDIT_PATH, { entries: entries.slice(0, MAX_ENTRIES) });
    console.log(`Updated ${AUDIT_PATH} (${entries.length} entries)`);
    break;
  }

  case "robot-telemetry": {
    requireKey(robotKey, payload.apiKey, "robot");
    const event = payload.event;
    if (!event || !event.tokenId) {
      fail("robot-telemetry: missing event.tokenId.");
    }
    const doc = readJson(EVENTS_PATH, { entries: [] });
    const entries = Array.isArray(doc.entries) ? doc.entries : [];
    entries.unshift(event);
    if (entries.length > MAX_ENTRIES) {
      entries.length = MAX_ENTRIES;
    }
    writeJson(EVENTS_PATH, { entries });
    console.log(`Appended telemetry for ${event.tokenId}`);
    break;
  }

  case "ping": {
    requireKey(operatorKey, payload.apiKey, "operator");
    console.log("ping ok");
    break;
  }

  default:
    fail(`Unsupported event type: ${eventType}`);
}
