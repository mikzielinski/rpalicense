#!/usr/bin/env node
/**
 * E2E: sample UiPath ZIP -> paranoid patch -> validate output archive.
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import JSZip from "jszip";
import { loadUiPathPatchRuntime, repoRoot } from "./lib/uipath-patch-loader.mjs";

const rt = loadUiPathPatchRuntime();
const {
  normalizeZipPath,
  findProjectJsonPath,
  projectDirFromJsonPath,
  listProjectXamlFiles,
  getEntryPointXamlFiles,
  isGhostLikeMode,
  removeExistingOpsRuntimeGate,
  removeExistingHiddenGates,
  injectTamperResistantGate,
  patchProjectJsonContent,
  getBundleLayout,
  buildProjectRobotEnv,
  buildProjectNugetConfig,
  buildProjectFeedSetupCmd,
  buildProjectSetupReadme,
  zipFolderPrefix
} = rt;

const NS_BLOCK_RE = /<TextExpression\.NamespacesForImplementation>/g;
const defaults = JSON.parse(readFileSync(join(repoRoot, "docs/panel.defaults.json"), "utf8"));
const sampleZipPath = join(repoRoot, "docs/assets/sample-uipath-project.zip");
const nupkgPath = join(repoRoot, defaults.nugetPackageUrl.replace(/^\.\//, "docs/"));

const cfg = {
  apiUrl: defaults.apiBaseUrl,
  pepper: defaults.pepper,
  nugetUrl: defaults.nugetPackageUrl,
  version: defaults.nugetVersion,
  graceDays: defaults.robotPackage?.graceDays ?? 7,
  telemetry: defaults.robotPackage?.telemetry !== false,
  killOnDeny: defaults.robotPackage?.killOnDeny !== false
};

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function readLocalBinary(url) {
  const rel = url.replace(/^\.\//, "");
  return readFileSync(join(repoRoot, "docs", rel));
}

async function patchUiPathProjectZip(zipBuffer, { mode, tokenValue }) {
  const zip = await JSZip.loadAsync(zipBuffer);
  const paths = Object.keys(zip.files).filter((p) => !zip.files[p].dir).map(normalizeZipPath);
  const projectJsonPath = findProjectJsonPath(paths);
  assert(projectJsonPath, "missing project.json");

  const projectDir = projectDirFromJsonPath(projectJsonPath);
  const projectJson = JSON.parse(await zip.file(projectJsonPath).async("string"));
  const xamlFiles = listProjectXamlFiles(paths, projectDir);
  assert(xamlFiles.length > 0, "missing xaml files");

  const patchedJson = patchProjectJsonContent(projectJson, cfg.version);
  const entryFiles = getEntryPointXamlFiles(xamlFiles, projectJson);
  const bundle = getBundleLayout(mode, projectDir, cfg.version);
  const projectSeed = projectJson.name ?? projectDir ?? "sample";
  const xamlRelPath = entryFiles.map((f) => f.relPath).join(", ");
  const modifiedXaml = new Map();

  if (isGhostLikeMode(mode)) {
    const entryPathSet = new Set(entryFiles.map((f) => f.fullPath));
    for (const { fullPath, relPath } of xamlFiles) {
      let content = await zip.file(fullPath).async("string");
      content = removeExistingOpsRuntimeGate(content);
      content = removeExistingHiddenGates(content);
      if (entryPathSet.has(fullPath)) {
        content = injectTamperResistantGate(content, tokenValue, `${projectSeed}:${relPath}`, mode).xaml;
      }
      modifiedXaml.set(normalizeZipPath(fullPath), content);
    }
  } else {
    throw new Error(`E2E supports ghost/paranoid only (got ${mode})`);
  }

  const outZip = new JSZip();
  const skipPrefixes = bundle.skipPrefixes;
  const writeTasks = [];

  for (const [path, entry] of Object.entries(zip.files)) {
    if (entry.dir) continue;
    const norm = normalizeZipPath(path);
    if (skipPrefixes.some((prefix) => prefix && norm.startsWith(prefix))) continue;

    if (norm === projectJsonPath) {
      outZip.file(path, `${JSON.stringify(patchedJson, null, 2)}\n`);
      continue;
    }
    if (modifiedXaml.has(norm)) {
      outZip.file(path, modifiedXaml.get(norm));
      continue;
    }
    writeTasks.push(entry.async("uint8array").then((data) => outZip.file(path, data)));
  }
  await Promise.all(writeTasks);

  const nupkgBuf = await readLocalBinary(cfg.nugetUrl);
  const paranoid = mode === "paranoid";
  outZip.file(bundle.nupkgPath, nupkgBuf);
  if (bundle.nupkgFlatPath) outZip.file(bundle.nupkgFlatPath, nupkgBuf);
  outZip.file(bundle.envPath, buildProjectRobotEnv(cfg, paranoid));
  outZip.file(bundle.nugetConfigPath, buildProjectNugetConfig(bundle.feedPath));
  outZip.file(bundle.operatorPath, buildProjectSetupReadme(cfg, mode, xamlRelPath, bundle));
  if (bundle.feedSetupCmdPath) {
    outZip.file(bundle.feedSetupCmdPath, buildProjectFeedSetupCmd(cfg.version, bundle.feedPath));
  }

  return {
    buffer: await outZip.generateAsync({ type: "nodebuffer", compression: "DEFLATE" }),
    projectJsonPath,
    projectDir,
    bundle,
    entryFiles
  };
}

let passed = 0;
let failed = 0;

async function run(name, fn) {
  try {
    await fn();
    console.log(`  ok  ${name}`);
    passed += 1;
  } catch (error) {
    console.error(`  FAIL ${name}`);
    console.error(`       ${error.message}`);
    failed += 1;
  }
}

console.log("UiPath patch E2E\n");

await run("paranoid patch produces valid ops archive", async () => {
  const input = readFileSync(sampleZipPath);
  const sourceNupkg = readFileSync(nupkgPath);
  const tokenValue = "e2e-test-license-token-001";

  const { buffer, projectJsonPath, projectDir, bundle, entryFiles } = await patchUiPathProjectZip(input, {
    mode: "paranoid",
    tokenValue
  });

  assert(buffer.length > input.length, "output should be larger than input");

  const out = await JSZip.loadAsync(buffer);
  const outPaths = Object.keys(out.files).filter((p) => !out.files[p].dir).map(normalizeZipPath);
  const prefix = zipFolderPrefix(projectDir);

  assert(outPaths.includes(projectJsonPath), "project.json missing");
  assert(outPaths.includes(`${prefix}nuget.config`), "nuget.config missing at project root");
  assert(outPaths.includes(bundle.nupkgPath), `missing hierarchical nupkg: ${bundle.nupkgPath}`);
  assert(outPaths.includes(bundle.nupkgFlatPath), `missing flat nupkg: ${bundle.nupkgFlatPath}`);
  assert(outPaths.includes(bundle.feedSetupCmdPath), `missing feed cmd: ${bundle.feedSetupCmdPath}`);
  assert(outPaths.includes(bundle.envPath), `missing env profile: ${bundle.envPath}`);
  assert(!outPaths.includes(`${prefix}USTAW-FEED-NUGET.cmd`), "cmd must not be in project root");

  const projectJson = JSON.parse(await out.file(projectJsonPath).async("string"));
  assert(
    projectJson.dependencies["UiPath.System.RoboticSecurity"] === `[${cfg.version}]`,
    JSON.stringify(projectJson.dependencies)
  );

  const nugetXml = await out.file(`${prefix}nuget.config`).async("string");
  assert(nugetXml.includes(".local/.nupkg"), "nuget.config feed path");

  const nupkgOut = await out.file(bundle.nupkgFlatPath).async("nodebuffer");
  assert(nupkgOut.length === sourceNupkg.length, "embedded nupkg size mismatch");

  const envText = await out.file(bundle.envPath).async("string");
  assert(envText.includes(`OPS_SEED_API_URL=${cfg.apiUrl}`), "env api url");
  assert(envText.includes(`OPS_SEED_PEPPER=${cfg.pepper}`), "env pepper");
  assert(envText.includes("OPS_SEED_GRACE_DAYS="), "env grace days");
  assert(!envText.includes("FLOW_RUNTIME_TOKEN"), "token must not be in env");

  for (const entry of entryFiles) {
    const xaml = await out.file(entry.fullPath).async("string");
    assert([...xaml.matchAll(NS_BLOCK_RE)].length === 1, `${entry.relPath}: duplicate NS block`);
    assert(xaml.includes("FromBase64String"), `${entry.relPath}: missing embedded token`);
    assert(xaml.includes("UiPath.System.RoboticSecurity"), `${entry.relPath}: missing gate ns`);
    assert(xaml.includes("Sequence.Variables"), `${entry.relPath}: missing variables`);
  }

  const cmd = await out.file(bundle.feedSetupCmdPath).async("string");
  assert(cmd.includes("UiPath.System.RoboticSecurity.1.0.7.nupkg"), "cmd references nupkg");
  assert(cmd.includes('%~dp0..\\.local\\.nupkg'), "cmd resolves feed from .project");
});

await run("double e2e patch stays idempotent on namespaces", async () => {
  const first = await patchUiPathProjectZip(readFileSync(sampleZipPath), {
    mode: "paranoid",
    tokenValue: "token-round-1"
  });
  const second = await patchUiPathProjectZip(first.buffer, {
    mode: "paranoid",
    tokenValue: "token-round-2"
  });

  const out = await JSZip.loadAsync(second.buffer);
  const mainPath = second.entryFiles[0].fullPath;
  const xaml = await out.file(mainPath).async("string");
  assert([...xaml.matchAll(NS_BLOCK_RE)].length === 1, "re-patch duplicated namespaces");
});

console.log(`\n${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
