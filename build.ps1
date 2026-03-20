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

$VSCODE_EXTENSION_DIR = ".\.vscode\extensions\stash-lang"

$artifacts = @{
    $INTERPRETER_SOURCE = $INTERPRETER_DEST
    $LSP_SOURCE         = $LSP_DEST
    $DAP_SOURCE         = $DAP_DEST
}

# ── Clean & Build ───────────────────────────────────────────────────
Write-Host "Cleaning and building the project..."

dotnet clean
if ($LASTEXITCODE -ne 0) { Write-Host "Clean failed."; exit 1 }
Write-Host "Clean complete. Starting build..."

# CLI — Native AOT
dotnet publish Stash.Cli -c Release -r $RUNTIME -p:PublishAot=true
if ($LASTEXITCODE -ne 0) { Write-Host "CLI build failed."; exit 1 }
Write-Host "CLI build complete."

# LSP — managed self-contained (NOT AOT, OmniSharp uses reflection-heavy DryIoc)
dotnet publish Stash.Lsp -c Release -r $RUNTIME
if ($LASTEXITCODE -ne 0) { Write-Host "LSP build failed."; exit 1 }

$lspSize = (Get-Item $LSP_SOURCE).Length
if ($lspSize -lt 20000000) {
    Write-Host "Error: LSP binary is only $lspSize bytes — expected >20MB for a managed build."
    Write-Host "The LSP appears to have been built with Native AOT, which is incompatible."
    Write-Host "Ensure Stash.Lsp.csproj has <PublishAot>false</PublishAot> and retry."
    exit 1
}
Write-Host "LSP build complete."

# DAP — managed self-contained (NOT AOT, OmniSharp uses reflection-heavy DryIoc)
dotnet publish Stash.Dap -c Release -r $RUNTIME
if ($LASTEXITCODE -ne 0) { Write-Host "DAP build failed."; exit 1 }

$dapSize = (Get-Item $DAP_SOURCE).Length
if ($dapSize -lt 20000000) {
    Write-Host "Error: DAP binary is only $dapSize bytes — expected >20MB for a managed build."
    Write-Host "The DAP appears to have been built with Native AOT, which is incompatible."
    Write-Host "Ensure Stash.Dap.csproj has <PublishAot>false</PublishAot> and retry."
    exit 1
}
Write-Host "DAP build complete."

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
