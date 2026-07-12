#Requires -Version 5.1
<#
.SYNOPSIS
  Full Windows test: patched sample -> UiPath Studio CLI publish -> verify gate DLL in .nupkg.

.USAGE
  cd C:\path\to\rpalicense
  npm install
  npm run test:uipath:all
  npm run test:uipath:integration
  .\scripts\windows\test-uipath-studio-publish.ps1
#>
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$StudioCmd = $env:UIPATH_STUDIO_CMD
if (-not $StudioCmd) {
    $candidates = @(
        "${env:ProgramFiles}\UiPath\Studio\UiPath.Studio.CommandLine.exe",
        "${env:ProgramFiles}\UiPath\Studio\Legacy\UiPath.Studio.CommandLine.exe",
        "${env:ProgramFiles(x86)}\UiPath\Studio\UiPath.Studio.CommandLine.exe",
        "${env:ProgramFiles(x86)}\UiPath\Studio\Legacy\UiPath.Studio.CommandLine.exe",
        "${env:LocalAppData}\Programs\UiPath\Studio\UiPath.Studio.CommandLine.exe"
    )
    $StudioCmd = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $StudioCmd -or -not (Test-Path $StudioCmd)) {
    Write-Error "UiPath.Studio.CommandLine.exe not found. Set UIPATH_STUDIO_CMD or install UiPath Studio."
}

Write-Host "UiPath CLI: $StudioCmd"
Push-Location $RepoRoot
$work = $null
try {
    npm run test:uipath:all
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $env:UIPATH_STUDIO_CMD = $StudioCmd
    node scripts/test-uipath-publish-integration.mjs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $work = Join-Path $env:TEMP ("ops-uipath-win-" + [guid]::NewGuid().ToString("n"))
    $publishOut = Join-Path $work "publish-out"
    New-Item -ItemType Directory -Path $publishOut -Force | Out-Null

    Write-Host "Extracting patched project to $work ..."
    node --input-type=module -e @"
import fs from 'node:fs';
import path from 'node:path';
import { patchUiPathProjectZip } from './scripts/lib/patch-uipath-project-zip.mjs';
import JSZip from 'jszip';

const root = process.argv[1];
const zipBuf = fs.readFileSync('docs/assets/sample-uipath-project.zip');
const { buffer, projectDir } = await patchUiPathProjectZip(zipBuf, {
  mode: 'paranoid',
  tokenValue: 'win-cli-test'
});
const zip = await JSZip.loadAsync(buffer);
const prefix = projectDir ? projectDir + '/' : '';
for (const [p, entry] of Object.entries(zip.files)) {
  if (entry.dir) continue;
  const rel = prefix && p.startsWith(prefix) ? p.slice(prefix.length) : p;
  const target = path.join(root, rel);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, await entry.async('nodebuffer'));
}
console.log('extracted to', root, 'projectDir=', projectDir);
"@ $work

    $projectJson = Join-Path $work "project.json"
    if (-not (Test-Path $projectJson)) {
        Write-Error "patched project.json missing at $projectJson"
    }

    Write-Host "Publishing via UiPath Studio CommandLine (Custom feed)..."
    & $StudioCmd publish `
        --project-path $projectJson `
        --target Custom `
        --feed $publishOut `
        --new-version "1.0.0-win-test"

    $nupkg = Get-ChildItem -Path $publishOut -Filter "*.nupkg" | Select-Object -First 1
    if (-not $nupkg) {
        Write-Error "No .nupkg produced in $publishOut"
    }

    Write-Host "Published: $($nupkg.FullName) ($($nupkg.Length) bytes)"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkg.FullName)
    try {
        $gate = @($zip.Entries | Where-Object { $_.FullName -match "UiPath\.System\.RoboticSecurity\.dll" })
        if ($gate.Count -eq 0) {
            $names = ($zip.Entries | ForEach-Object { $_.FullName }) -join "`n  "
            Write-Error "Gate DLL not found in published nupkg. Entries:`n  $names"
        }
        Write-Host "OK: gate DLL in publish output: $($gate[0].FullName)"
    } finally {
        $zip.Dispose()
    }

    Write-Host ""
    Write-Host "All Windows UiPath publish checks passed."
} finally {
    Pop-Location
    if ($work -and (Test-Path $work)) {
        for ($i = 0; $i -lt 5; $i++) {
            try {
                Start-Sleep -Seconds 1
                Remove-Item -Recurse -Force $work -ErrorAction Stop
                break
            } catch {
                if ($i -eq 4) {
                    Write-Warning "Could not remove temp dir $work : $_"
                }
            }
        }
    }
}
