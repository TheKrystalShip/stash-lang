#Requires -Version 5.1
<#
.SYNOPSIS
    Stash installer for Windows.

.DESCRIPTION
    Downloads the Stash CLI, stash-check, and stash-format binaries from the
    latest GitHub Release, verifies their SHA-256 checksums, and installs them
    into a user-local directory.

.PARAMETER Version
    Specific version tag to install (e.g. v0.1.0). Defaults to latest.

.PARAMETER InstallDir
    Override install directory. Defaults to $env:USERPROFILE\.stash\bin.

.PARAMETER NoVerify
    Skip SHA-256 verification. NOT recommended.

.EXAMPLE
    irm https://raw.githubusercontent.com/TheKrystalShip/stash-lang/main/install.ps1 | iex

.EXAMPLE
    .\install.ps1 -Version v0.1.0
#>
[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$InstallDir = (Join-Path $env:USERPROFILE ".stash\bin"),
    [switch]$NoVerify
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Repo = "TheKrystalShip/stash-lang"

function Info($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Fail($msg) { Write-Host "error: $msg" -ForegroundColor Red; exit 1 }

# Detect arch.
$arch = switch ($env:PROCESSOR_ARCHITECTURE) {
    "AMD64" { "x64" }
    "ARM64" { Fail "Windows arm64 is not yet published. Track progress in the roadmap." }
    default { Fail "unsupported architecture: $env:PROCESSOR_ARCHITECTURE" }
}

# Resolve version.
if ($Version -eq "latest") {
    Info "Resolving latest release..."
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -UseBasicParsing
        $Version = $release.tag_name
    } catch {
        Fail "could not resolve latest release: $_. The project may not have published any releases yet."
    }
}

Info "Installing Stash $Version for windows-$arch into $InstallDir"

$cliAsset       = "stash-windows-$arch.exe"
$checkAsset     = "stash-check-windows-$arch.exe"
$formatAsset    = "stash-format-windows-$arch.exe"
$checksumsAsset = "checksums-sha256.txt"

$baseUrl = "https://github.com/$Repo/releases/download/$Version"
$tmp = Join-Path $env:TEMP "stash-install-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

try {
    foreach ($asset in @($cliAsset, $checkAsset, $formatAsset, $checksumsAsset)) {
        Info "Downloading $asset..."
        try {
            Invoke-WebRequest -Uri "$baseUrl/$asset" -OutFile (Join-Path $tmp $asset) -UseBasicParsing
        } catch {
            Fail "failed to download $asset from $baseUrl. Is the release published and the asset name correct? ($_)"
        }
    }

    if ($NoVerify) {
        Info "WARNING: skipping SHA-256 verification (-NoVerify)"
    } else {
        Info "Verifying SHA-256 checksums..."
        $checksums = Get-Content (Join-Path $tmp $checksumsAsset)
        foreach ($asset in @($cliAsset, $checkAsset, $formatAsset)) {
            $line = $checksums | Where-Object { $_ -match "  $([regex]::Escape($asset))$" }
            if (-not $line) { Fail "no checksum found for $asset" }
            $expected = ($line -split '\s+')[0]
            $actual = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $tmp $asset)).Hash.ToLower()
            if ($actual -ne $expected.ToLower()) {
                Fail "checksum mismatch for ${asset}:`n  expected: $expected`n  actual:   $actual"
            }
        }
        Info "Checksums verified."
    }

    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Copy-Item (Join-Path $tmp $cliAsset)    (Join-Path $InstallDir "stash.exe")        -Force
    Copy-Item (Join-Path $tmp $checkAsset)  (Join-Path $InstallDir "stash-check.exe")  -Force
    Copy-Item (Join-Path $tmp $formatAsset) (Join-Path $InstallDir "stash-format.exe") -Force

    Info "Installed:"
    Write-Host "    $InstallDir\stash.exe"
    Write-Host "    $InstallDir\stash-check.exe"
    Write-Host "    $InstallDir\stash-format.exe"

    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($userPath -notlike "*$InstallDir*") {
        Write-Host ""
        Info "Adding $InstallDir to your user PATH..."
        [Environment]::SetEnvironmentVariable("Path", "$userPath;$InstallDir", "User")
        Write-Host "    Open a new terminal for the PATH change to take effect."
    }

    Write-Host ""
    Info "Done. Run 'stash --version' to verify (in a new shell)."
}
finally {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $tmp
}
