/**
 * Shared paranoid/ghost patch pipeline for tests (sample ZIP -> patched buffer).
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import JSZip from "jszip";
import { loadUiPathPatchRuntime, repoRoot } from "./uipath-patch-loader.mjs";

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
  buildLocalNugetConfig,
  buildDirectoryBuildProps,
  buildDirectoryBuildTargets,
  buildProjectSetupReadme,
  writeGlobalPackagesCache,
  writeBundledGateAssembly,
  zipFolderPrefix
} = rt;

const defaults = JSON.parse(readFileSync(join(repoRoot, "docs/panel.defaults.json"), "utf8"));

export const defaultPatchCfg = {
  apiUrl: defaults.apiBaseUrl,
  pepper: defaults.pepper,
  nugetUrl: defaults.nugetPackageUrl,
  version: defaults.nugetVersion,
  graceDays: defaults.robotPackage?.graceDays ?? 7,
  telemetry: defaults.robotPackage?.telemetry !== false,
  killOnDeny: defaults.robotPackage?.killOnDeny !== false
};

export async function readLocalBinary(url) {
  const rel = url.replace(/^\.\//, "");
  return readFileSync(join(repoRoot, "docs", rel));
}

export async function patchUiPathProjectZip(zipBuffer, { mode, tokenValue, cfg = defaultPatchCfg }) {
  const zip = await JSZip.loadAsync(zipBuffer);
  const paths = Object.keys(zip.files).filter((p) => !zip.files[p].dir).map(normalizeZipPath);
  const projectJsonPath = findProjectJsonPath(paths);
  if (!projectJsonPath) throw new Error("missing project.json");

  const projectDir = projectDirFromJsonPath(projectJsonPath);
  const projectJson = JSON.parse(await zip.file(projectJsonPath).async("string"));
  const xamlFiles = listProjectXamlFiles(paths, projectDir);
  if (!xamlFiles.length) throw new Error("missing xaml files");

  const patchedJson = patchProjectJsonContent(projectJson, cfg.version, {
    omitNugetDependency: mode === "paranoid"
  });
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
    throw new Error(`patch supports ghost/paranoid only (got ${mode})`);
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

  const prefix = zipFolderPrefix(projectDir);
  const nupkgBuf = await readLocalBinary(cfg.nugetUrl);
  const paranoid = mode === "paranoid";
  outZip.file(bundle.nupkgPath, nupkgBuf);
  if (bundle.nupkgFlatPath) outZip.file(bundle.nupkgFlatPath, nupkgBuf);
  await writeGlobalPackagesCache(outZip, prefix, nupkgBuf, cfg.version);
  await writeBundledGateAssembly(outZip, prefix, nupkgBuf);
  outZip.file(bundle.envPath, buildProjectRobotEnv(cfg, paranoid));
  outZip.file(bundle.nugetConfigPath, buildProjectNugetConfig(bundle.feedPath));
  if (bundle.localNugetConfigPath) {
    outZip.file(bundle.localNugetConfigPath, buildLocalNugetConfig());
  }
  outZip.file(bundle.directoryBuildPropsPath, buildDirectoryBuildProps(bundle.feedPath));
  if (bundle.directoryBuildTargetsPath) {
    outZip.file(bundle.directoryBuildTargetsPath, buildDirectoryBuildTargets());
  }
  outZip.file(bundle.operatorPath, buildProjectSetupReadme(cfg, mode, xamlRelPath, bundle));

  return {
    buffer: await outZip.generateAsync({ type: "nodebuffer", compression: "DEFLATE" }),
    projectJsonPath,
    projectDir,
    bundle,
    entryFiles,
    projectJsonBefore: projectJson,
    projectJsonAfter: patchedJson
  };
}

export { repoRoot, normalizeZipPath, zipFolderPrefix };
