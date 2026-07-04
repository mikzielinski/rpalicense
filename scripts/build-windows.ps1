#Requires -Version 5.1
<#
.SYNOPSIS
    Buduje Ops.Runtime.Seed i przygotowuje folder OpsRuntime pod UiPath.

.DESCRIPTION
    1. (opcjonalnie) generuje seed.jwt z fixture'ow testowych
    2. buduje biblioteke Release
    3. pakuje NuGet
    4. kopiuje DLL, nupkg i pliki testowe do lokalnego folderu

.PARAMETER OutputRoot
    Docelowy folder (domyslnie %USERPROFILE%\OpsRuntime, bez uprawnien administratora).

.PARAMETER GenerateFixtures
    Generuje seed.live.jwt z katalogu test-fixtures (wymaga .NET 8 dla keygen).

.PARAMETER SkipBuild
    Pomin build — tylko kopiowanie juz zbudowanych artefaktow.

.EXAMPLE
    .\scripts\build-windows.ps1

.EXAMPLE
    .\scripts\build-windows.ps1 -GenerateFixtures -OutputRoot D:\OpsRuntime
#>
[CmdletBinding()]
param(
    [string]$OutputRoot = '',
    [switch]$GenerateFixtures,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $env:USERPROFILE 'OpsRuntime'
}

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Project = Join-Path $Root 'src\Ops.Runtime.Seed\Ops.Runtime.Seed.csproj'
$KeygenProject = Join-Path $Root 'keygen\SeedForge.csproj'
$ConfigPath = Join-Path $Root 'test-fixtures\test-config.json'
$CatalogLive = Join-Path $Root 'test-fixtures\catalog\live.json'
$KeysDir = Join-Path $Root 'test-fixtures\keys'
$SeedLiveJwt = Join-Path $Root 'test-fixtures\seed.live.jwt'

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Initialize-OutputRoot {
    Write-Step "Tworzenie folderu docelowego: $OutputRoot"
    try {
        New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
        $probe = Join-Path $OutputRoot '.write-test'
        'ok' | Set-Content -Path $probe -Encoding ASCII
        Remove-Item -Path $probe -Force
    }
    catch {
        throw @"
Nie mozna utworzyc folderu: $OutputRoot
Powod: $($_.Exception.Message)

Sprobuj recznie:
  mkdir "$OutputRoot"
lub uruchom z innym folderem:
  .\scripts\build-windows.ps1 -OutputRoot "D:\OpsRuntime"
"@
    }

    foreach ($sub in @('nuget', 'lib', 'catalog')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $OutputRoot $sub) | Out-Null
    }
    Write-Host "    OK: $OutputRoot" -ForegroundColor Green
}

function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
    }
}

function Get-TestConfig {
    if (-not (Test-Path $ConfigPath)) {
        throw "Brak pliku konfiguracji testowej: $ConfigPath"
    }
    return Get-Content $ConfigPath -Raw | ConvertFrom-Json
}

function New-Fixtures {
    $cfg = Get-TestConfig
    $hosts = if ($cfg.hosts -and $cfg.hosts.Count -gt 0) { $cfg.hosts -join ',' } else { '' }

    Write-Step 'Generowanie fixture testowych (keygen)'

    if (-not (Test-Path (Join-Path $KeysDir 'seal.private.pem'))) {
        throw "Brak kluczy RSA w $KeysDir — uruchom najpierw: dotnet run --project keygen -- newkeys test-fixtures/keys"
    }

    $payload = Join-Path $Root 'sample\payload.example.json'
    $issueArgs = @(
        'run', '--project', $KeygenProject, '-c', 'Release', '--',
        'issue',
        (Join-Path $KeysDir 'seal.private.pem'),
        $cfg.pepper,
        $cfg.tokenId,
        $cfg.owner,
        $cfg.validToUtc,
        $hosts,
        $payload
    )
    $entryJson = & dotnet @issueArgs
    if ($LASTEXITCODE -ne 0) { throw 'keygen issue failed' }

    $catalogDir = Split-Path $CatalogLive -Parent
    New-Item -ItemType Directory -Force -Path $catalogDir | Out-Null
    "{`"entries`":[$entryJson]}" | Set-Content -Path $CatalogLive -Encoding UTF8 -NoNewline

    $wrapArgs = @(
        'run', '--project', $KeygenProject, '-c', 'Release', '--',
        'wrapjwt',
        $CatalogLive,
        $cfg.envelopeSigningKey,
        $cfg.envelopePepper,
        $cfg.envelopeIssuer,
        $cfg.envelopeAudience,
        $cfg.validToUtc
    )
    $raw = & dotnet @wrapArgs 2>$null
    $jwt = ($raw | Where-Object { $_ -match '^eyJ' } | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or -not $jwt) { throw 'keygen wrapjwt failed' }

    $jwt.Trim() | Set-Content -Path $SeedLiveJwt -Encoding ASCII -NoNewline
    Write-Host "    tokenId:  $($cfg.tokenId)"
    Write-Host "    jwt:      $SeedLiveJwt"
}

function Ensure-SeedJwt {
    if (Test-Path $SeedLiveJwt) {
        return $SeedLiveJwt
    }

    if (-not (Test-Path $CatalogLive)) {
        Write-Warning 'Brak seed.live.jwt i catalog/live.json — pomijam kopiowanie katalogu offline.'
        return $null
    }

    Write-Step 'Pakowanie seed.jwt z istniejacego catalog/live.json'
    $cfg = Get-TestConfig
    $wrapArgs = @(
        'run', '--project', $KeygenProject, '-c', 'Release', '--',
        'wrapjwt',
        $CatalogLive,
        $cfg.envelopeSigningKey,
        $cfg.envelopePepper,
        $cfg.envelopeIssuer,
        $cfg.envelopeAudience,
        $cfg.validToUtc
    )
    $raw = & dotnet @wrapArgs 2>$null
    $jwt = ($raw | Where-Object { $_ -match '^eyJ' } | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or -not $jwt) { throw 'keygen wrapjwt failed' }

    $jwt.Trim() | Set-Content -Path $SeedLiveJwt -Encoding ASCII -NoNewline
    return $SeedLiveJwt
}

function Copy-BuildArtifacts {
    param(
        [string]$DllSource,
        [string]$NuGetSource
    )

    $nugetDir = Join-Path $OutputRoot 'nuget'
    $libDir = Join-Path $OutputRoot 'lib'
    $catalogDir = Join-Path $OutputRoot 'catalog'

    New-Item -ItemType Directory -Force -Path $nugetDir, $libDir, $catalogDir | Out-Null

    Copy-Item -Path $DllSource -Destination (Join-Path $libDir 'Ops.Runtime.Seed.dll') -Force
    Copy-Item -Path $NuGetSource -Destination $nugetDir -Force

    $seedSource = Ensure-SeedJwt
    if ($seedSource) {
        Copy-Item -Path $seedSource -Destination (Join-Path $catalogDir 'seed.jwt') -Force
    }

    if (Test-Path $ConfigPath) {
        Copy-Item -Path $ConfigPath -Destination (Join-Path $OutputRoot 'test-config.json') -Force
    }

    $envFile = Join-Path $OutputRoot 'offline-env.txt'
    if (Test-Path $ConfigPath) {
        $cfg = Get-TestConfig
        @"
# Zmienne srodowiskowe do testu offline (Panel systemu Windows lub setx)
OPS_SEED_CATALOG_FILE=$(Join-Path $catalogDir 'seed.jwt')
OPS_SEED_PEPPER=$($cfg.pepper)
OPS_SEED_ENVELOPE_PEPPER=$($cfg.envelopePepper)
OPS_SEED_ENVELOPE_SIGNING_KEY=$($cfg.envelopeSigningKey)

# Token testowy (Orchestrator Asset lub Invoke Code):
# $($cfg.tokenId)
"@ | Set-Content -Path $envFile -Encoding UTF8
    }

    return [PSCustomObject]@{
        NuGetDir = $nugetDir
        LibDir = $libDir
        CatalogDir = $catalogDir
        Dll = Join-Path $libDir 'Ops.Runtime.Seed.dll'
        NuGet = Get-ChildItem (Join-Path $nugetDir '*.nupkg') | Select-Object -First 1 -ExpandProperty FullName
        SeedJwt = if ($seedSource) { Join-Path $catalogDir 'seed.jwt' } else { $null }
        EnvFile = $envFile
    }
}

Write-Host '========================================' -ForegroundColor Yellow
Write-Host ' Ops.Runtime.Seed — Windows / UiPath build' -ForegroundColor Yellow
Write-Host '========================================' -ForegroundColor Yellow
Write-Host "Repo:   $Root"
Write-Host "Output: $OutputRoot"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK nie jest zainstalowany. Zainstaluj .NET SDK 6.0+ z https://dotnet.microsoft.com/download'
}

Initialize-OutputRoot

if ($GenerateFixtures) {
    New-Fixtures
}

if (-not $SkipBuild) {
    Write-Step 'Build biblioteki (Release)'
    Invoke-DotNet @('build', $Project, '-c', 'Release')

    Write-Step 'Pack NuGet'
    $packOut = Join-Path $Root 'artifacts\nupkg'
    New-Item -ItemType Directory -Force -Path $packOut | Out-Null
    Invoke-DotNet @('pack', $Project, '-c', 'Release', '--no-build', '-o', $packOut)
}

$dllSource = Join-Path $Root 'src\Ops.Runtime.Seed\bin\Release\net6.0\Ops.Runtime.Seed.dll'
$nupkgSource = Get-ChildItem (Join-Path $Root 'artifacts\nupkg\*.nupkg') -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not (Test-Path $dllSource)) {
    throw "Nie znaleziono DLL: $dllSource — uruchom build bez -SkipBuild"
}
if (-not $nupkgSource) {
    throw 'Nie znaleziono pliku .nupkg w artifacts\nupkg — uruchom build bez -SkipBuild'
}

Write-Step 'Kopiowanie do folderu UiPath'
$out = Copy-BuildArtifacts -DllSource $dllSource -NuGetSource $nupkgSource.FullName

if (-not (Test-Path $out.Dll)) {
    throw "Kopiowanie nie powiodlo sie — brak pliku: $($out.Dll)"
}

Write-Host ''
Write-Host '========================================' -ForegroundColor Green
Write-Host ' Gotowe' -ForegroundColor Green
Write-Host '========================================' -ForegroundColor Green
Write-Host "  Folder:     $OutputRoot"
Write-Host "  DLL:        $($out.Dll)"
Write-Host "  NuGet:      $($out.NuGet)"
if ($out.SeedJwt) {
    Write-Host "  seed.jwt:   $($out.SeedJwt)"
    Write-Host "  env vars:   $($out.EnvFile)"
}
Write-Host ''
Write-Host 'UiPath Studio:' -ForegroundColor White
Write-Host "  1. Settings -> Manage Sources -> dodaj: $($out.NuGetDir)"
Write-Host '  2. Manage Packages -> zainstaluj Ops.Runtime.Seed'
Write-Host '  3. Projekt: Windows (.NET), nie Legacy'
if (Test-Path $ConfigPath) {
    $token = (Get-TestConfig).tokenId
    Write-Host "  4. Asset Orchestrator: RuntimeToken = $token"
}
Write-Host ''
Write-Host 'Invoke Code (test):' -ForegroundColor White
Write-Host @'
  var token = "RT-TEST-REPORT-001"; // lub z Orchestrator Asset
  if (!Ops.Runtime.Seed.Bootstrapper.TryInitialize(token, out var profile))
      throw new System.Exception(Ops.Runtime.Seed.Bootstrapper.LastCheck.Code);
  System.Console.WriteLine(profile.ApiEndpoint);
'@
