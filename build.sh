#!/usr/bin/env bash
set -euo pipefail

# ── Detect runtime ──────────────────────────────────────────────────
OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
    Linux)  RID_OS="linux" ;;
    Darwin) RID_OS="osx"   ;;
    *)      echo "Unsupported OS: $OS"; exit 1 ;;
esac

case "$ARCH" in
    x86_64)  RID_ARCH="x64"   ;;
    aarch64) RID_ARCH="arm64" ;;
    arm64)   RID_ARCH="arm64" ;;
    *)       echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac

RUNTIME="${RID_OS}-${RID_ARCH}"

# ── Paths ───────────────────────────────────────────────────────────
LSP_SOURCE="./Stash.Lsp/bin/Release/net10.0/${RUNTIME}/publish/StashLsp"
LSP_DEST="${HOME}/.local/bin/stash-lsp"

DAP_SOURCE="./Stash.Dap/bin/Release/net10.0/${RUNTIME}/publish/StashDap"
DAP_DEST="${HOME}/.local/bin/stash-dap"

INTERPRETER_SOURCE="./Stash.Cli/bin/Release/net10.0/${RUNTIME}/publish/Stash"
INTERPRETER_DEST="${HOME}/.local/bin/stash"

VSCODE_EXTENSION_DIR="./.vscode/extensions/stash-lang"

# ── Clean & Build ───────────────────────────────────────────────────
echo "Cleaning and building the project..."

dotnet clean
echo "Clean complete. Starting build..."

# CLI — Native AOT
dotnet publish Stash.Cli -c Release -r "$RUNTIME" -p:PublishAot=true
echo "CLI build complete."

# LSP — managed self-contained (NOT AOT, OmniSharp uses reflection-heavy DryIoc)
dotnet publish Stash.Lsp -c Release -r "$RUNTIME"

LSP_SIZE=$(stat -c%s "$LSP_SOURCE" 2>/dev/null || stat -f%z "$LSP_SOURCE")
if [ "$LSP_SIZE" -lt 20000000 ]; then
    echo "Error: LSP binary is only ${LSP_SIZE} bytes — expected >20MB for a managed build."
    echo "The LSP appears to have been built with Native AOT, which is incompatible."
    echo "Ensure Stash.Lsp.csproj has <PublishAot>false</PublishAot> and retry."
    exit 1
fi
echo "LSP build complete."

# DAP — managed self-contained (NOT AOT, OmniSharp uses reflection-heavy DryIoc)
dotnet publish Stash.Dap -c Release -r "$RUNTIME"

DAP_SIZE=$(stat -c%s "$DAP_SOURCE" 2>/dev/null || stat -f%z "$DAP_SOURCE")
if [ "$DAP_SIZE" -lt 20000000 ]; then
    echo "Error: DAP binary is only ${DAP_SIZE} bytes — expected >20MB for a managed build."
    echo "The DAP appears to have been built with Native AOT, which is incompatible."
    echo "Ensure Stash.Dap.csproj has <PublishAot>false</PublishAot> and retry."
    exit 1
fi
echo "DAP build complete."

# ── Deploy ──────────────────────────────────────────────────────────
mkdir -p "${HOME}/.local/bin"

declare -A ARTIFACTS=(
    ["$INTERPRETER_SOURCE"]="$INTERPRETER_DEST"
    ["$LSP_SOURCE"]="$LSP_DEST"
    ["$DAP_SOURCE"]="$DAP_DEST"
)

for source in "${!ARTIFACTS[@]}"; do
    dest="${ARTIFACTS[$source]}"
    if [ ! -f "$source" ]; then
        echo "Error: Source binary not found at ${source}"
        echo "Run the build step first."
        exit 1
    fi
    rm -f "$dest"
    cp "$source" "$dest"
    echo "Copied ${source} to ${dest}."
done

echo "Deployment complete."

# ── Build VS Code Extension ────────────────────────────────────────
echo "Building VSCode extension..."
pushd "$VSCODE_EXTENSION_DIR" > /dev/null

if ! npx tsc; then
    echo "VSCode extension build failed."
    popd > /dev/null
    exit 1
fi

popd > /dev/null
echo "Build complete."
