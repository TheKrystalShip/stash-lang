#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Detect runtime ──────────────────────────────────────────────────
$arch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq "Arm64") { "arm64" } else { "x64" }
$RUNTIME = "win-$arch"

# ── Paths ───────────────────────────────────────────────────────────
$LSP_SOURCE = ".\Stash.Lsp\bin\Release\net10.0\$RUNTIME\publish\StashLsp.exe"
$LSP_DEST = "$env:USERPROFILE\.local\bin\stash-lsp.exe"

$DAP_SOURCE = ".\Stash.Dap\bin\Release\net10.0\$RUNTIME\publish\StashDap.exe"
$DAP_DEST = "$env:USERPROFILE\.local\bin\stash-dap.exe"

$INTERPRETER_SOURCE = ".\Stash.Cli\bin\Release\net10.0\$RUNTIME\publish\Stash.exe"
$INTERPRETER_DEST = "$env:USERPROFILE\.local\bin\stash.exe"

$REGISTRY_SOURCE = ".\Stash.Registry\bin\Release\net10.0\$RUNTIME\publish\StashRegistry.exe"
$REGISTRY_DEST = "$env:USERPROFILE\.local\bin\stash-registry.exe"

$CHECK_SOURCE = ".\Stash.Check\bin\Release\net10.0\$RUNTIME\publish\StashCheck.exe"
$CHECK_DEST = "$env:USERPROFILE\.local\bin\stash-check.exe"

$FORMAT_SOURCE = ".\Stash.Format\bin\Release\net10.0\$RUNTIME\publish\StashFormat.exe"
$FORMAT_DEST = "$env:USERPROFILE\.local\bin\stash-format.exe"

$VSCODE_EXTENSION_DIR = ".\.vscode\extensions\stash-lang"

$artifacts = @{
    $INTERPRETER_SOURCE = $INTERPRETER_DEST
    $LSP_SOURCE         = $LSP_DEST
    $DAP_SOURCE         = $DAP_DEST
    $REGISTRY_SOURCE    = $REGISTRY_DEST
    $CHECK_SOURCE       = $CHECK_DEST
    $FORMAT_SOURCE     = $FORMAT_DEST
}

# ── Clean & Build ───────────────────────────────────────────────────
Write-Host "Cleaning and building the project..."

dotnet clean
if ($LASTEXITCODE -ne 0) { Write-Host "Clean failed."; exit 1 }
Write-Host "Clean complete. Starting build..."

dotnet publish -c Release -r $RUNTIME --self-contained
if ($LASTEXITCODE -ne 0) { Write-Host "CLI build failed."; exit 1 }
Write-Host "CLI build complete."

# LSP — managed self-contained (NOT AOT, OmniSharp uses reflection-heavy DryIoc)
dotnet publish Stash.Lsp -c Release -r $RUNTIME --self-contained
if ($LASTEXITCODE -ne 0) { Write-Host "LSP build failed."; exit 1 }

# ── Deploy ──────────────────────────────────────────────────────────
$binDir = "$env:USERPROFILE\.local\bin"
if (-not (Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir -Force | Out-Null }

foreach ($source in $artifacts.Keys) {
    $dest = $artifacts[$source]
    if (-not (Test-Path $source)) {
        Write-Host "Error: Source binary not found at $source"
        Write-Host "Run the build step first."
        exit 1
    }
    if (Test-Path $dest) { Remove-Item $dest -Force }
    Copy-Item $source $dest
    Write-Host "Copied $source to $dest."
}

Write-Host "Deployment complete."

# ── Build VS Code Extension ────────────────────────────────────────
Write-Host "Building VSCode extension..."
Push-Location $VSCODE_EXTENSION_DIR

npx tsc
if ($LASTEXITCODE -ne 0) {
    Write-Host "VSCode extension build failed."
    Pop-Location
    exit 1
}

Pop-Location
Write-Host "Build complete."
