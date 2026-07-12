#!/usr/bin/env node
/**
 * Tests UiPath project patch helpers from docs/app.js (XAML namespaces, bundle layout).
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { loadUiPathPatchRuntime, repoRoot } from "./lib/uipath-patch-loader.mjs";

const sampleXaml = readFileSync(
  join(repoRoot, "docs/assets/sample-uipath-project/Main.xaml"),
  "utf8"
);

const {
  injectTamperResistantGate,
  injectEmbeddedGateIntoXaml,
  getBundleLayout,
  buildProjectNugetConfig,
  buildDirectoryBuildProps,
  buildDirectoryBuildTargets,
  ensureXamlImports,
  patchProjectJsonContent
} = loadUiPathPatchRuntime();

const NS_BLOCK_RE = /<TextExpression\.NamespacesForImplementation>/g;
const REF_BLOCK_RE = /<TextExpression\.ReferencesForImplementation>/g;

function countMatches(text, re) {
  return [...text.matchAll(re)].length;
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function test(name, fn) {
  try {
    fn();
    console.log(`  ok  ${name}`);
    return true;
  } catch (error) {
    console.error(`  FAIL ${name}`);
    console.error(`       ${error.message}`);
    return false;
  }
}

let passed = 0;
let failed = 0;

function run(name, fn) {
  if (test(name, fn)) passed += 1;
  else failed += 1;
}

console.log("UiPath patch tests\n");

run("merges duplicate NamespacesForImplementation blocks", () => {
  const duplicate = sampleXaml.replace(
    "</TextExpression.NamespacesForImplementation>",
    "</TextExpression.NamespacesForImplementation>\n" +
      "  <TextExpression.NamespacesForImplementation>\n" +
      "    <sco:Collection x:TypeArguments=\"x:String\">\n" +
      "      <x:String>System.Linq</x:String>\n" +
      "    </sco:Collection>\n" +
      "  </TextExpression.NamespacesForImplementation>"
  );
  assert(countMatches(duplicate, NS_BLOCK_RE) === 2, "fixture should start with 2 blocks");

  const merged = ensureXamlImports(duplicate, ["UiPath.System.RoboticSecurity"]);
  assert(countMatches(merged, NS_BLOCK_RE) === 1, `expected 1 block, got ${countMatches(merged, NS_BLOCK_RE)}`);
  assert(merged.includes("<x:String>System.Linq</x:String>"), "should keep namespaces from both blocks");
  assert(merged.includes("<x:String>UiPath.System.RoboticSecurity</x:String>"), "should add required namespace");
});

run("paranoid inject keeps assembly ref without duplicate namespace block", () => {
  const { xaml } = injectTamperResistantGate(sampleXaml, "token-abc-123", "Main.xaml", "paranoid");
  assert(countMatches(xaml, NS_BLOCK_RE) === 1, `expected 1 NS block, got ${countMatches(xaml, NS_BLOCK_RE)}`);
  assert(countMatches(xaml, REF_BLOCK_RE) === 1, `expected 1 ref block, got ${countMatches(xaml, REF_BLOCK_RE)}`);
  assert(!xaml.includes("<x:String>UiPath.System.RoboticSecurity</x:String>"), "FQN expression must not add namespace import");
  assert(xaml.includes("<AssemblyReference>UiPath.System.RoboticSecurity</AssemblyReference>"), "missing assembly ref");
  assert(xaml.includes("Sequence.Variables"), "missing variables section");
  assert(xaml.includes("FromBase64String"), "paranoid should embed base64 token expression");
  assert(xaml.includes("Global.System.Convert.FromBase64String"), "VB must disambiguate System from UiPath.System");
});

run("double inject is idempotent for namespace blocks", () => {
  const first = injectEmbeddedGateIntoXaml(sampleXaml, "token-1", "_bindingTraceId", "ghost");
  const second = injectEmbeddedGateIntoXaml(first.xaml, "token-2", "_bindingTraceId", "ghost");
  assert(countMatches(second.xaml, NS_BLOCK_RE) === 1, "second inject must not duplicate namespaces");
  assert(countMatches(second.xaml, REF_BLOCK_RE) === 1, "second inject must not duplicate references");
});

run("paranoid bundle ships lib DLL and publish targets (no .cmd)", () => {
  const bundle = getBundleLayout("paranoid", "testDRM", "1.0.7");
  assert(bundle.libDllPath === "testDRM/lib/UiPath.System.RoboticSecurity.dll", bundle.libDllPath);
  assert(bundle.directoryBuildTargetsPath === "testDRM/Directory.Build.targets", bundle.directoryBuildTargetsPath);
  assert(bundle.nupkgPath === "testDRM/.project/UiPath.System.RoboticSecurity.1.0.7.nupkg", bundle.nupkgPath);
  assert(bundle.nugetConfigPath === null, "paranoid must not ship NuGet.Config");
  assert(bundle.directoryBuildPropsPath === null, "paranoid must not ship Directory.Build.props");
  assert(!("openCmdPath" in bundle), "OTWORZ-PROJEKT.cmd must not exist");
  assert(!("bootstrapCmdPath" in bundle), "bootstrap-feed.cmd must not be required");
});

run("NuGet.Config clears global feeds and wires local feed", () => {
  const xml = buildProjectNugetConfig(".local/.nupkg");
  assert(xml.includes("<clear />"), "must clear inherited global feeds");
  assert(xml.includes('globalPackagesFolder" value=".packages"'), "missing globalPackagesFolder");
  assert(xml.includes('value=".local/.nupkg"'), "feed path missing in NuGet.Config");
  assert(xml.includes("api.nuget.org"), "must include nuget.org");
  assert(xml.includes("UiPath-Official"), "must include UiPath official feed");
});

run("Directory.Build.props forces RestoreSources and config file", () => {
  const props = buildDirectoryBuildProps(".local/.nupkg");
  assert(props.includes("RestorePackagesPath"), "missing RestorePackagesPath");
  assert(props.includes("RestoreConfigFile"), "missing RestoreConfigFile");
  assert(props.includes("RestoreSources"), "missing RestoreSources");
  assert(props.includes(".local/.nupkg"), "missing project feed in RestoreSources");
});

run("Directory.Build.targets packs gate DLL for Orchestrator publish", () => {
  const targets = buildDirectoryBuildTargets();
  assert(targets.includes('Pack="true"'), "must pack lib DLL into nupkg");
  assert(targets.includes("lib/net6.0"), "package path for robot runtime");
  assert(targets.includes("UiPath.System.RoboticSecurity.dll"), "gate DLL");
});

run("paranoid project.json omits NuGet dependency", () => {
  const json = patchProjectJsonContent({ name: "testDRM", dependencies: {} }, "1.0.7", {
    omitNugetDependency: true
  });
  assert(!("UiPath.System.RoboticSecurity" in json.dependencies), JSON.stringify(json.dependencies));
});

run("ghost mode keeps NuGet dependency in project.json", () => {
  const json = patchProjectJsonContent({ name: "testDRM", dependencies: {} }, "1.0.7");
  assert(json.dependencies["UiPath.System.RoboticSecurity"] === "[1.0.7]", JSON.stringify(json.dependencies));
});

console.log(`\n${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
