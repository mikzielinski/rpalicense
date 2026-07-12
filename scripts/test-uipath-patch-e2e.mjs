#!/usr/bin/env node
/**
 * E2E: sample UiPath ZIP -> paranoid patch -> validate zero-config output archive.
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import JSZip from "jszip";
import { repoRoot } from "./lib/uipath-patch-loader.mjs";
import { defaultPatchCfg, patchUiPathProjectZip } from "./lib/patch-uipath-project-zip.mjs";

const NS_BLOCK_RE = /<TextExpression\.NamespacesForImplementation>/g;
const defaults = JSON.parse(readFileSync(join(repoRoot, "docs/panel.defaults.json"), "utf8"));
const sampleZipPath = join(repoRoot, "docs/assets/sample-uipath-project.zip");
const nupkgPath = join(repoRoot, defaults.nugetPackageUrl.replace(/^\.\//, "docs/"));

const cfg = defaultPatchCfg;

function assert(condition, message) {
  if (!condition) throw new Error(message);
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

await run("paranoid patch is zero-config ready (open Main.xaml)", async () => {
  const input = readFileSync(sampleZipPath);
  const sourceNupkg = readFileSync(nupkgPath);
  const tokenValue = "e2e-test-license-token-001";

  const { buffer, projectJsonPath, projectDir, bundle, entryFiles, projectJsonBefore } =
    await patchUiPathProjectZip(input, { mode: "paranoid", tokenValue });

  assert(buffer.length > input.length, "output should be larger than input");

  const out = await JSZip.loadAsync(buffer);
  const outPaths = Object.keys(out.files).filter((p) => !out.files[p].dir);
  const prefix = projectDir ? `${projectDir}/` : "";
  const archiveNupkg = `${prefix}.project/UiPath.System.RoboticSecurity.${cfg.version}.nupkg`;

  assert(outPaths.includes(projectJsonPath), "project.json missing");
  assert(outPaths.includes(`${prefix}lib/UiPath.System.RoboticSecurity.dll`), "bundled gate DLL missing");
  assert(outPaths.includes(`${prefix}Directory.Build.targets`), "Directory.Build.targets missing");
  assert(outPaths.includes(archiveNupkg), `archive nupkg missing: ${archiveNupkg}`);
  assert(!outPaths.includes(`${prefix}NuGet.Config`), "paranoid must not ship NuGet.Config");
  assert(!outPaths.includes(`${prefix}Directory.Build.props`), "paranoid must not ship Directory.Build.props");
  assert(!outPaths.some((p) => p.includes(".packages/")), "paranoid must not ship .packages cache");
  assert(!outPaths.some((p) => p.includes(".local/")), "paranoid must not ship .local nuget feed");
  assert(!outPaths.includes(`${prefix}OTWORZ-PROJEKT.cmd`), "must not require OTWORZ-PROJEKT.cmd");
  assert(!outPaths.some((p) => p.includes("bootstrap-feed.cmd")), "must not require bootstrap-feed.cmd");
  assert(!outPaths.some((p) => p.endsWith("USTAW-FEED-NUGET.cmd")), "manual feed cmd must not exist");

  const projectJson = JSON.parse(await out.file(projectJsonPath).async("string"));
  assert(projectJson.main === projectJsonBefore.main, "main entry must stay Main.xaml");
  assert(
    !("UiPath.System.RoboticSecurity" in (projectJson.dependencies ?? {})),
    "paranoid must not add NuGet dependency — Studio ignores project feeds"
  );

  const dll = await out.file(`${prefix}lib/UiPath.System.RoboticSecurity.dll`).async("nodebuffer");
  assert(dll.length > 1000, "bundled dll too small");

  const nupkgOut = await out.file(archiveNupkg).async("nodebuffer");
  assert(nupkgOut.length === sourceNupkg.length, "embedded archive nupkg size mismatch");

  const envText = await out.file(bundle.envPath).async("string");
  assert(envText.includes(`OPS_SEED_API_URL=${cfg.apiUrl}`), "env api url");
  assert(!envText.includes("FLOW_RUNTIME_TOKEN"), "token must not be in env");

  const signature = await out.file(bundle.operatorPath).async("string");
  assert(signature.includes("no NuGet.Config / .packages"), "operator signature should document slim bundle");

  for (const entry of entryFiles) {
    const xaml = await out.file(entry.fullPath).async("string");
    assert([...xaml.matchAll(NS_BLOCK_RE)].length === 1, `${entry.relPath}: duplicate NS block`);
    assert(xaml.includes("FromBase64String"), `${entry.relPath}: missing embedded token`);
  }
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
