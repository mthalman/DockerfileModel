# setup.ps1 — Install elan (Lean version manager), build the Lean project, and run tests.
#
# Idempotent: safe to re-run. Skips elan install if already present.
# Works on Windows with PowerShell 5.1+.
#
# Usage:
#   .\lean\setup.ps1

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "=== Lean 4 Setup ===" -ForegroundColor Cyan
Write-Host "Project directory: $ScriptDir"
Write-Host ""

# --- Step 1: Install elan (if not already installed) ---
$elanCmd = Get-Command elan -ErrorAction SilentlyContinue
if ($elanCmd) {
    $elanVersion = & elan --version 2>&1
    Write-Host "[OK] elan is already installed: $elanVersion" -ForegroundColor Green
} else {
    Write-Host "[..] Installing elan (Lean version manager)..." -ForegroundColor Yellow

    $elanInstallerUrl = "https://raw.githubusercontent.com/leanprover/elan/master/elan-init.ps1"
    $installerPath = Join-Path $env:TEMP "elan-init.ps1"

    try {
        Invoke-WebRequest -Uri $elanInstallerUrl -OutFile $installerPath -UseBasicParsing
    } catch {
        Write-Host "[!!] Failed to download elan installer. Trying alternative method..." -ForegroundColor Red
        # Alternative: download the Windows binary directly
        $elanExeUrl = "https://github.com/leanprover/elan/releases/latest/download/elan-x86_64-pc-windows-msvc.zip"
        $zipPath = Join-Path $env:TEMP "elan.zip"
        $extractDir = Join-Path $env:USERPROFILE ".elan" "bin"

        if (-not (Test-Path $extractDir)) {
            New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
        }

        Invoke-WebRequest -Uri $elanExeUrl -OutFile $zipPath -UseBasicParsing
        Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force
        Remove-Item $zipPath -Force

        Write-Host "[OK] elan binary extracted to $extractDir" -ForegroundColor Green
        # Add to PATH for this session
        $env:PATH = "$extractDir;$env:PATH"

        # Run elan to set up toolchain management
        & elan self update 2>&1 | Out-Null
    }

    if (Test-Path $installerPath) {
        # Run the PowerShell installer with default toolchain = none (lean-toolchain handles it)
        & powershell -ExecutionPolicy Bypass -File $installerPath -y --default-toolchain none
        Remove-Item $installerPath -Force -ErrorAction SilentlyContinue
    }

    # Add elan to PATH for this session
    $elanBinDir = Join-Path $env:USERPROFILE ".elan" "bin"
    if (Test-Path $elanBinDir) {
        $env:PATH = "$elanBinDir;$env:PATH"
    }

    $elanCmd = Get-Command elan -ErrorAction SilentlyContinue
    if ($elanCmd) {
        $elanVersion = & elan --version 2>&1
        Write-Host "[OK] elan installed: $elanVersion" -ForegroundColor Green
    } else {
        Write-Host "[!!] elan installation may have succeeded, but 'elan' is not on PATH." -ForegroundColor Red
        Write-Host "     Please restart your terminal and re-run this script." -ForegroundColor Red
        Write-Host "     Expected location: $elanBinDir" -ForegroundColor Red
        exit 1
    }
}

# --- Step 2: Show which Lean toolchain will be used ---
$toolchain = Get-Content (Join-Path $ScriptDir "lean-toolchain") -Raw
$toolchain = $toolchain.Trim()
Write-Host ""
Write-Host "[..] Lean toolchain pinned to: $toolchain" -ForegroundColor Yellow
Write-Host "     elan will download this automatically on first use."

# --- Step 3: Build the project ---
Write-Host ""
Write-Host "[..] Building Lean project (lake build)..." -ForegroundColor Yellow
Push-Location $ScriptDir
try {
    & lake build
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[!!] Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "[OK] Build succeeded." -ForegroundColor Green

    # --- Step 4: Run the tests ---
    Write-Host ""
    Write-Host "[..] Running property tests (lake exe DockerfileModelTests)..." -ForegroundColor Yellow
    & lake exe DockerfileModelTests
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[!!] Tests failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "[OK] Tests passed." -ForegroundColor Green
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "=== Setup complete ===" -ForegroundColor Cyan
