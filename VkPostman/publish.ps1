#!/usr/bin/env pwsh
# ------------------------------------------------------------------------------
# VK Postman — single-file, framework-dependent release publisher.
#
#   - Produces ONE file:  publish/VkPostman.exe
#   - Framework-dependent: the target machine needs .NET 10 Desktop Runtime
#       (https://dotnet.microsoft.com/download/dotnet/10.0 → "Desktop Runtime x64")
#   - Compressed (Brotli) to minimize download size.
#   - No pdb sidecar (symbols are embedded for smaller footprint).
#
# Usage:
#   pwsh -File .\publish.ps1
#   pwsh -File .\publish.ps1 -Clean                # wipe publish/ first
#   pwsh -File .\publish.ps1 -Runtime win-arm64    # for ARM64 Windows
#   pwsh -File .\publish.ps1 -ReadyToRun           # precompile to machine code (larger exe, faster start)
# ------------------------------------------------------------------------------

[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string]$Runtime = 'win-x64',

    [string]$OutputDir = 'publish',

    [switch]$ReadyToRun,

    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$projectFile = Join-Path $PSScriptRoot 'VkPostman.Wpf/VkPostman.Wpf.csproj'
$outputPath  = Join-Path $PSScriptRoot $OutputDir

if (-not (Test-Path $projectFile)) {
    throw "Couldn't find $projectFile — run this from the repo root."
}

if ($Clean -and (Test-Path $outputPath)) {
    Write-Host "→ Cleaning $outputPath..." -ForegroundColor DarkGray
    Remove-Item -Recurse -Force $outputPath
}

# MSBuild properties that together produce ONE exe with no sidecars.
# Note: EnableCompressionInSingleFile is only valid with self-contained; omitted
# here because framework-dependent output is already small (no runtime bundled).
$props = @(
    'PublishSingleFile=true'                      # wrap everything in the exe
    'SelfContained=false'                         # framework-dependent (requires .NET Desktop Runtime)
    'IncludeAllContentForSelfExtract=true'        # pack native libs + content into the exe too
    'DebugType=embedded'                          # fold the pdb into the exe
    'DebugSymbols=true'
    'SatelliteResourceLanguages=en'               # drop localized resource DLLs we don't ship
)
if ($ReadyToRun) {
    $props += 'PublishReadyToRun=true'
}

$publishArgs = @(
    'publish'
    $projectFile
    '-c', 'Release'
    '-r', $Runtime
    '-o', $outputPath
    '--nologo'
)
foreach ($p in $props) { $publishArgs += "-p:$p" }

Write-Host ''
Write-Host "→ dotnet publish ($Runtime, framework-dependent, single file)..." -ForegroundColor Cyan

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Report on the result.
$exe = Join-Path $outputPath 'VkPostman.exe'
if (-not (Test-Path $exe)) {
    throw "Expected $exe after publish — something went sideways."
}

$sizeMB = [math]::Round((Get-Item $exe).Length / 1MB, 2)

# Anything besides the exe that leaked out is worth surfacing.
$extras = Get-ChildItem $outputPath -File | Where-Object { $_.Name -ne 'VkPostman.exe' }

Write-Host ''
Write-Host "✓ Built:  $exe" -ForegroundColor Green
Write-Host "  Size:   $sizeMB MB"
Write-Host "  Target: $Runtime, framework-dependent"
Write-Host "  Needs:  .NET 10 Desktop Runtime on the target machine"
Write-Host "          (https://dotnet.microsoft.com/download/dotnet/10.0)"
if ($extras.Count -gt 0) {
    Write-Host ''
    Write-Host "  Extra files alongside the exe (couldn't be packed):" -ForegroundColor Yellow
    foreach ($f in $extras) { Write-Host "    - $($f.Name) ($([math]::Round($f.Length/1KB,1)) KB)" }
}
