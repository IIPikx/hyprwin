# =============================================================
#  HyprWin Build & Package Script
#  Publishes the app + builds the Inno Setup installer.
# =============================================================
#
# Usage:
#   .\publish\build-installer.ps1
#   .\publish\build-installer.ps1 -Version "1.2.0"
#   .\publish\build-installer.ps1 -SkipPublish   # Skip dotnet publish
#   .\publish\build-installer.ps1 -SkipSign      # Skip code signing
#   .\publish\build-installer.ps1 -PfxPath "cert.pfx" -PfxPassword "pw"
#
# Prerequisites:
#   - .NET 8 SDK
#   - Inno Setup 6 (https://jrsoftware.org/isinfo.php)

param(
    [string]$Version     = "1.0.0",
    [switch]$SkipPublish,
    [switch]$SkipSign,
    [string]$PfxPath     = "",
    [string]$PfxPassword = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot  # repo root (one level up from publish/)

Write-Host "=======================================" -ForegroundColor Cyan
Write-Host " HyprWin Build & Package -- v$Version"  -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan

# -- Step 1: dotnet publish --
# NOTE: SingleFile is intentionally disabled for WPF apps.
# WPF's native PresentationNative DLLs do not always extract correctly
# from a single-file bundle, causing DllNotFoundException at runtime.
# The installer packages the whole publish\bin\ folder instead.
if (-not $SkipPublish) {
    Write-Host "`n[1/4] Publishing HyprWin (Release, win-x64, self-contained)..." -ForegroundColor Yellow
    dotnet publish "$root\src\HyprWin.App\HyprWin.App.csproj" `
        -c Release -r win-x64 --self-contained true `
        -o "$root\publish\bin"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    $fileCount = (Get-ChildItem "$root\publish\bin\*" -Recurse -File).Count
    Write-Host "  Published $fileCount files to: publish\bin\" -ForegroundColor Green
} else {
    Write-Host "`n[1/4] Skipping dotnet publish (-SkipPublish)" -ForegroundColor DarkGray
}

# -- Step 1b: Add publish\bin to Defender exclusions (dev convenience) --
try {
    $binDir = "$root\publish\bin"
    if (Test-Path $binDir) {
        Add-MpPreference -ExclusionPath $binDir -ErrorAction SilentlyContinue
        Write-Host "  Defender exclusion added for: $binDir" -ForegroundColor DarkGray
    }
} catch {
    Write-Host "  (Defender exclusion skipped -- may need elevation)" -ForegroundColor DarkGray
}

# -- Step 2: Code-sign the EXE --
if (-not $SkipSign) {
    Write-Host "`n[2/4] Code-signing the EXE..." -ForegroundColor Yellow

    $exePath = "$root\publish\bin\HyprWin.App.exe"

    # Locate signtool.exe from the installed Windows SDK
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" `
                    -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending |
                Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) {
        $signtool = Get-ChildItem "$env:ProgramFiles\Windows Kits\10\bin\*\x64\signtool.exe" `
                        -ErrorAction SilentlyContinue |
                    Sort-Object FullName -Descending |
                    Select-Object -First 1 -ExpandProperty FullName
    }

    if (-not $signtool) {
        # Fall back to PowerShell's built-in Set-AuthenticodeSignature (no SDK needed)
        Write-Host "  signtool.exe not found -- using Set-AuthenticodeSignature..." -ForegroundColor DarkGray

        $existingCert = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object { $_.Subject -like "*HyprWin*" -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
            Select-Object -First 1

        if (-not $existingCert) {
            Write-Host "  Creating self-signed code-signing certificate..." -ForegroundColor DarkGray
            $existingCert = New-SelfSignedCertificate `
                -Subject "CN=HyprWin, O=HyprWin Contributors, C=DE" `
                -CertStoreLocation "Cert:\CurrentUser\My" `
                -Type CodeSigning `
                -HashAlgorithm SHA256 `
                -KeyUsage DigitalSignature `
                -FriendlyName "HyprWin Code Signing" `
                -NotAfter (Get-Date).AddYears(5)
        }

        $result = Set-AuthenticodeSignature -FilePath $exePath `
                    -Certificate $existingCert `
                    -HashAlgorithm SHA256 `
                    -TimestampServer "http://timestamp.digicert.com" `
                    -ErrorAction SilentlyContinue
        if (-not $result -or ($result.Status -ne "Valid" -and $result.Status -ne "UnknownError")) {
            # Retry without timestamp server (offline fallback)
            $result = Set-AuthenticodeSignature -FilePath $exePath `
                        -Certificate $existingCert `
                        -HashAlgorithm SHA256
        }
        Write-Host "  Signed (self-signed): $($result.Status)" -ForegroundColor Green
        Write-Host "  NOTE: For public distribution, use a trusted CA certificate." -ForegroundColor DarkGray

    } elseif ($PfxPath -and (Test-Path $PfxPath)) {
        Write-Host "  Signing with provided PFX: $PfxPath" -ForegroundColor DarkGray
        $signArgs = @("sign", "/fd", "SHA256", "/f", $PfxPath,
                      "/tr", "http://timestamp.digicert.com", "/td", "SHA256")
        if ($PfxPassword) { $signArgs += @("/p", $PfxPassword) }
        $signArgs += $exePath
        & $signtool @signArgs
        if ($LASTEXITCODE -ne 0) { Write-Host "  Warning: signtool returned $LASTEXITCODE" -ForegroundColor Yellow }
        else { Write-Host "  Signed successfully." -ForegroundColor Green }

    } else {
        Write-Host "  No PFX provided -- generating self-signed certificate..." -ForegroundColor DarkGray
        $selfSignedPfx = "$root\publish\HyprWin-SelfSigned.pfx"
        $certPassword  = ConvertTo-SecureString -String "HyprWin-Build-2026" -Force -AsPlainText

        $existingCert = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object { $_.Subject -like "*HyprWin*" -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
            Select-Object -First 1

        if (-not $existingCert) {
            $existingCert = New-SelfSignedCertificate `
                -Subject "CN=HyprWin, O=HyprWin Contributors, C=DE" `
                -CertStoreLocation "Cert:\CurrentUser\My" `
                -Type CodeSigning `
                -HashAlgorithm SHA256 `
                -KeyUsage DigitalSignature `
                -FriendlyName "HyprWin Code Signing" `
                -NotAfter (Get-Date).AddYears(5)
        }

        Export-PfxCertificate -Cert $existingCert -FilePath $selfSignedPfx -Password $certPassword | Out-Null

        & $signtool sign /fd SHA256 /f $selfSignedPfx /p "HyprWin-Build-2026" `
            /tr "http://timestamp.digicert.com" /td SHA256 /d "HyprWin" $exePath
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  Timestamp server unreachable -- retrying without timestamp..." -ForegroundColor Yellow
            & $signtool sign /fd SHA256 /f $selfSignedPfx /p "HyprWin-Build-2026" /d "HyprWin" $exePath
        }
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Signed with self-signed certificate." -ForegroundColor Green
            Write-Host "  NOTE: For public distribution, use a trusted CA certificate." -ForegroundColor DarkGray
        }
        Remove-Item $selfSignedPfx -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "`n[2/4] Skipping code signing (-SkipSign)" -ForegroundColor DarkGray
}

# -- Step 3: Find Inno Setup compiler --
Write-Host "`n[3/4] Looking for Inno Setup 6 compiler..." -ForegroundColor Yellow
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

# -- Step 4: Compile installer --
Write-Host "`n[4/4] Compiling installer..." -ForegroundColor Yellow

$installerDir = "$root\installer"
if (-not (Test-Path $installerDir)) {
    New-Item -ItemType Directory -Path $installerDir | Out-Null
}

& $iscc "/DMyAppVersion=$Version" "$root\publish\hyprwin-setup.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

$installerFile = "$installerDir\HyprWin-Setup-$Version.exe"
if (Test-Path $installerFile) {
    $size = [math]::Round((Get-Item $installerFile).Length / 1MB, 1)
    $hash = (Get-FileHash $installerFile -Algorithm SHA256).Hash

    Write-Host "`n=======================================" -ForegroundColor Green
    Write-Host " Installer built successfully!"           -ForegroundColor Green
    Write-Host "  File:   $installerFile"                -ForegroundColor Green
    Write-Host "  Size:   ${size} MB"                    -ForegroundColor Green
    Write-Host "  SHA256: $hash"                         -ForegroundColor Green
    Write-Host ""                                         -ForegroundColor Green
    Write-Host " For winget submission, use this SHA256"  -ForegroundColor Green
    Write-Host " in publish\winget\HyprWin.HyprWin.installer.yaml" -ForegroundColor Green
    Write-Host "=======================================" -ForegroundColor Green
} else {
    Write-Host "  Warning: installer file not found at expected path" -ForegroundColor Yellow
}