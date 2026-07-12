import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import vm from "node:vm";
import JSZip from "jszip";

const root = join(dirname(fileURLToPath(import.meta.url)), "../..");

export function loadUiPathPatchRuntime(appJsPath = join(root, "docs/app.js")) {
  const appJs = readFileSync(appJsPath, "utf8");
  const markers = {
    inject: appJs.indexOf("const OPS_RUNTIME_GATE_ID"),
    mid1: appJs.indexOf("function injectRefsOnlyIntoXaml(xamlText"),
    xaml: appJs.indexOf("const XAML_NS_BLOCK_RE"),
    xamlEnd: appJs.indexOf("function findSequenceInsertIndex(xaml)"),
    parse: appJs.indexOf("function normalizeZipPath(path)"),
    parseEnd: appJs.indexOf("function renderUiPathProjectXamlSelect(xamlFiles"),
    json: appJs.indexOf("function patchProjectJsonContent(projectJson, version)"),
    jsonEnd: appJs.indexOf("async function patchUiPathProjectAndDownload()")
  };

  for (const [key, value] of Object.entries(markers)) {
    if (value === -1) {
      throw new Error(`Could not locate ${key} marker in docs/app.js`);
    }
  }

  const chunks = [
    appJs.slice(markers.inject, markers.mid1),
    appJs.slice(markers.xaml, markers.xamlEnd),
    appJs.slice(markers.parse, markers.parseEnd),
    appJs.slice(markers.json, markers.jsonEnd)
  ];

  const sandbox = {
    console,
    TextEncoder,
    JSZip,
    btoa: (binary) => Buffer.from(binary, "binary").toString("base64")
  };

  for (const chunk of chunks) {
    vm.runInNewContext(chunk, sandbox);
  }

  return sandbox;
}

export const repoRoot = root;
