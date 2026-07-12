#!/usr/bin/env node
/**
 * Integration: patched project on disk -> MSBuild pack (publish simulation) -> DLL probe.
 * UiPath.Studio.CommandLine.exe requires Windows + Studio install (see scripts/windows/).
 */
import {
  mkdtempSync,
  mkdirSync,
  readFileSync,
  rmSync,
  writeFileSync
} from "node:fs";
import { execSync } from "node:child_process";
import { tmpdir } from "node:os";
import { join } from "node:path";
import JSZip from "jszip";
import { patchUiPathProjectZip, repoRoot, zipFolderPrefix } from "./lib/patch-uipath-project-zip.mjs";

const sampleZipPath = join(repoRoot, "docs/assets/sample-uipath-project.zip");

let passed = 0;
let failed = 0;

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function run(name, fn) {
  try {
    await fn();
    console.log(`  ok  ${name}`);
    passed += 1;
  } catch (error) {
    console.error(`  FAIL ${name}`);
    console.error(`       ${error.message}`);
    if (error.stdout) console.error(String(error.stdout).slice(0, 2000));
    if (error.stderr) console.error(String(error.stderr).slice(0, 2000));
    failed += 1;
  }
}

async function extractPatchedProject(rootDir) {
  const { buffer, projectDir } = await patchUiPathProjectZip(readFileSync(sampleZipPath), {
    mode: "paranoid",
    tokenValue: "integration-test-token-42"
  });
  const zip = await JSZip.loadAsync(buffer);
  const prefix = zipFolderPrefix(projectDir);
  for (const [path, entry] of Object.entries(zip.files)) {
    if (entry.dir) continue;
    const rel = path.startsWith(prefix) ? path.slice(prefix.length) : path;
    const target = join(rootDir, rel);
    mkdirSync(join(target, ".."), { recursive: true });
    writeFileSync(target, await entry.async("nodebuffer"));
  }
  return { projectDir, projectRoot: rootDir };
}

const ASSEMBLY_PROBE_CS = `
using System;
using System.Reflection;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: AssemblyProbe <path-to-dll>");
            return 2;
        }

        var asm = Assembly.LoadFrom(args[0]);
        var bootstrapper = asm.GetType("UiPath.System.RoboticSecurity.Bootstrapper");
        if (bootstrapper is null)
        {
            Console.Error.WriteLine("Bootstrapper type missing");
            return 1;
        }

        var tryInit = Array.Find(bootstrapper.GetMethods(), m => m.Name == "TryInitialize" && m.IsStatic);
        if (tryInit is null)
        {
            Console.Error.WriteLine("TryInitialize method missing");
            return 1;
        }

        Console.WriteLine("Bootstrapper OK");
        return 0;
    }
}
`;

const PACK_PROBE_CSPROJ = `<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <PackageId>OpsRuntime.ProcessPackProbe</PackageId>
    <Version>1.0.0</Version>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
  </PropertyGroup>
</Project>
`;

console.log("UiPath publish integration\n");

await run("extract patched project to disk", async () => {
  const root = mkdtempSync(join(tmpdir(), "ops-uipath-patch-"));
  try {
    const { projectRoot } = await extractPatchedProject(root);
    assert(readFileSync(join(projectRoot, "project.json"), "utf8").includes("SampleOpsProject"), "project.json");
    assert(readFileSync(join(projectRoot, "lib/UiPath.System.RoboticSecurity.dll")).length > 1000, "lib dll");
    assert(readFileSync(join(projectRoot, "Directory.Build.targets"), "utf8").includes('Pack="true"'), "targets");
    const json = JSON.parse(readFileSync(join(projectRoot, "project.json"), "utf8"));
    assert(!json.dependencies?.["UiPath.System.RoboticSecurity"], "no nuget dep in project.json");
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

await run("MSBuild pack includes gate DLL (Orchestrator publish simulation)", async () => {
  const root = mkdtempSync(join(tmpdir(), "ops-uipath-pack-"));
  try {
    await extractPatchedProject(root);
    writeFileSync(join(root, "PackProbe.csproj"), PACK_PROBE_CSPROJ);
    const outDir = join(root, "pack-out");
    mkdirSync(outDir, { recursive: true });

    execSync(`dotnet pack "${join(root, "PackProbe.csproj")}" -o "${outDir}" --nologo -v q`, {
      stdio: "pipe",
      encoding: "utf8"
    });

    const nupkg = readFileSync(join(outDir, "OpsRuntime.ProcessPackProbe.1.0.0.nupkg"));
    const inner = await JSZip.loadAsync(nupkg);
    const paths = Object.keys(inner.files).filter((p) => !inner.files[p].dir);
    assert(
      paths.some((p) => p.toLowerCase().includes("uipath.system.roboticsecurity.dll")),
      `gate DLL not in packed nupkg: ${paths.join(", ")}`
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

await run("gate DLL exposes Bootstrapper.TryInitialize (runtime probe)", async () => {
  const root = mkdtempSync(join(tmpdir(), "ops-uipath-asm-"));
  const probeDir = join(root, "probe");
  mkdirSync(probeDir, { recursive: true });
  try {
    const { projectRoot } = await extractPatchedProject(root);
    const dllPath = join(projectRoot, "lib/UiPath.System.RoboticSecurity.dll");
    writeFileSync(join(probeDir, "AssemblyProbe.csproj"), `<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net6.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
`);
    writeFileSync(join(probeDir, "Program.cs"), ASSEMBLY_PROBE_CS);
    execSync(`dotnet run --project "${join(probeDir, "AssemblyProbe.csproj")}" -- "${dllPath}"`, {
      stdio: "pipe",
      encoding: "utf8"
    });
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

await run("patched Main.xaml has embedded gate + assembly reference", async () => {
  const { buffer } = await patchUiPathProjectZip(readFileSync(sampleZipPath), {
    mode: "paranoid",
    tokenValue: "xaml-gate-token"
  });
  const zip = await JSZip.loadAsync(buffer);
  const xaml = await zip.file("sample-uipath-project/Main.xaml").async("string");
  assert(xaml.includes("FromBase64String"), "embedded token");
  assert(xaml.includes("<AssemblyReference>UiPath.System.RoboticSecurity</AssemblyReference>"), "asm ref");
  assert(!xaml.includes("<x:String>UiPath.System.RoboticSecurity</x:String>"), "no redundant ns import");
});

const uipathCli = process.env.UIPATH_STUDIO_CMD
  ?? "C:\\Program Files\\UiPath\\Studio\\UiPath.Studio.CommandLine.exe";

await run("UiPath Studio CommandLine publish (Windows only — skipped on Linux CI)", async () => {
  if (process.platform !== "win32") {
    console.log("       (skip: no Windows / UiPath Studio in this environment)");
    return;
  }
  try {
    execSync(`"${uipathCli}" --help`, { stdio: "pipe" });
  } catch {
    console.log("       (skip: UiPath.Studio.CommandLine.exe not installed)");
    return;
  }

  const root = mkdtempSync(join(tmpdir(), "ops-uipath-studio-"));
  try {
    const { projectRoot } = await extractPatchedProject(root);
    const publishOut = join(root, "publish-out");
    mkdirSync(publishOut, { recursive: true });
    execSync(
      `"${uipathCli}" publish --project-path "${join(projectRoot, "project.json")}" --target Custom --feed "${publishOut}" --new-version 1.0.0-integration`,
      { stdio: "pipe", encoding: "utf8", timeout: 300000 }
    );
    const files = readFileSync(publishOut, { encoding: "utf8", withFileTypes: true });
    void files;
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

console.log(`\n${passed} passed, ${failed} failed`);
if (failed) process.exit(1);
