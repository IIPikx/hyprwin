# ╔══════════════════════════════════════════════════════════════╗
# ║              HyprWin Build & Package Script                  ║
# ║   Publishes the app + builds the Inno Setup installer.       ║
# ╚══════════════════════════════════════════════════════════════╝
#
# Usage:
#   .\publish\build-installer.ps1
#   .\publish\build-installer.ps1 -Version "1.2.0"
#   .\publish\build-installer.ps1 -SkipPublish   # Skip dotnet publish
#
# Prerequisites:
#   - .NET 8 SDK
#   - Inno Setup 6 (https://jrsoftware.org/isinfo.php)

param(
    [string]$Version = "1.0.0",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot  # repo root (one level up from publish/)

Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host " HyprWin Build & Package — v$Version"    -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

# ── Step 1: dotnet publish ──
if (-not $SkipPublish) {
    Write-Host "`n[1/3] Publishing HyprWin (Release, win-x64, self-contained)..." -ForegroundColor Yellow
    dotnet publish "$root\src\HyprWin.App\HyprWin.App.csproj" `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -o "$root\publish\bin"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    Write-Host "  Published to: publish\bin\" -ForegroundColor Green
} else {
    Write-Host "`n[1/3] Skipping dotnet publish (--SkipPublish)" -ForegroundColor DarkGray
}

# ── Step 2: Find Inno Setup compiler ──
Write-Host "`n[2/3] Looking for Inno Setup 6 compiler..." -ForegroundColor Yellow
$isccPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Host @"

  Inno Setup 6 not found. Install it from:
    https://jrsoftware.org/isdl.php

  Or install via winget:
    winget install JRSoftware.InnoSetup

  After installing, re-run this script.
"@ -ForegroundColor Red
    exit 1
}

Write-Host "  Found: $iscc" -ForegroundColor Green

# ── Step 3: Compile installer ──
Write-Host "`n[3/3] Compiling installer..." -ForegroundColor Yellow

# Create output directory
$installerDir = "$root\installer"
if (-not (Test-Path $installerDir)) {
    New-Item -ItemType Directory -Path $installerDir | Out-Null
}

# Pass version as a define to the .iss script
& $iscc "/DMyAppVersion=$Version" "$root\publish\hyprwin-setup.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

$installerFile = "$installerDir\HyprWin-Setup-$Version.exe"
if (Test-Path $installerFile) {
    $size = [math]::Round((Get-Item $installerFile).Length / 1MB, 1)
    $hash = (Get-FileHash $installerFile -Algorithm SHA256).Hash

    Write-Host "`n═══════════════════════════════════════" -ForegroundColor Green
    Write-Host " Installer built successfully!" -ForegroundColor Green
    Write-Host "  File:   $installerFile" -ForegroundColor Green
    Write-Host "  Size:   ${size} MB" -ForegroundColor Green
    Write-Host "  SHA256: $hash" -ForegroundColor Green
    Write-Host "" -ForegroundColor Green
    Write-Host " For winget submission, use this SHA256 hash" -ForegroundColor Green
    Write-Host " in publish\winget\HyprWin.HyprWin.installer.yaml" -ForegroundColor Green
    Write-Host "═══════════════════════════════════════" -ForegroundColor Green
} else {
    Write-Host "  Warning: installer file not found at expected path" -ForegroundColor Yellow
}
