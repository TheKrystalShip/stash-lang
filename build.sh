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

REGISTRY_SOURCE="./Stash.Registry/bin/Release/net10.0/${RUNTIME}/publish/StashRegistry"
REGISTRY_DEST="${HOME}/.local/bin/stash-registry"

VSCODE_EXTENSION_DIR="./.vscode/extensions/stash-lang"

# ── Clean & Build ───────────────────────────────────────────────────
echo "Cleaning and building the project..."

dotnet clean
echo "Clean complete. Starting build..."

dotnet publish -c Release -r "$RUNTIME" --self-contained
echo "Build complete."

# ── Deploy ──────────────────────────────────────────────────────────
mkdir -p "${HOME}/.local/bin"

declare -A ARTIFACTS=(
    ["$INTERPRETER_SOURCE"]="$INTERPRETER_DEST"
    ["$LSP_SOURCE"]="$LSP_DEST"
    ["$DAP_SOURCE"]="$DAP_DEST"
    ["$REGISTRY_SOURCE"]="$REGISTRY_DEST"
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
