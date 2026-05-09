#!/usr/bin/env bash
# Stash installer for Linux and macOS.
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/TheKrystalShip/stash-lang/main/install.sh | bash
#   curl -fsSL https://raw.githubusercontent.com/TheKrystalShip/stash-lang/main/install.sh | bash -s -- --version v0.1.0
#
# Environment variables:
#   STASH_INSTALL_DIR  Override install directory (default: $HOME/.stash/bin)
#   STASH_VERSION      Override version (default: latest)
#   STASH_NO_VERIFY    Set to 1 to skip SHA-256 verification (NOT recommended)

set -euo pipefail

REPO="TheKrystalShip/stash-lang"
INSTALL_DIR="${STASH_INSTALL_DIR:-$HOME/.stash/bin}"
VERSION="${STASH_VERSION:-latest}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) VERSION="$2"; shift 2;;
        --dir) INSTALL_DIR="$2"; shift 2;;
        --no-verify) STASH_NO_VERIFY=1; shift;;
        -h|--help)
            sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *) echo "Unknown option: $1" >&2; exit 2;;
    esac
done

err()  { echo "error: $*" >&2; exit 1; }
info() { echo "==> $*"; }

# Detect platform.
uname_s="$(uname -s)"
uname_m="$(uname -m)"

case "$uname_s" in
    Linux)  os="linux";;
    Darwin) os="macos";;
    *) err "unsupported OS: $uname_s. Use install.ps1 on Windows.";;
esac

case "$uname_m" in
    x86_64|amd64) arch="x64";;
    arm64|aarch64)
        if [[ "$os" != "macos" ]]; then
            err "arm64 builds are currently only published for macOS. Linux arm64 is on the roadmap."
        fi
        arch="arm64"
        ;;
    *) err "unsupported architecture: $uname_m";;
esac

# Resolve version.
if [[ "$VERSION" == "latest" ]]; then
    info "Resolving latest release..."
    VERSION="$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" | grep '"tag_name"' | head -1 | cut -d'"' -f4)"
    [[ -z "$VERSION" ]] && err "could not resolve latest release. The project may not have published any releases yet."
fi

info "Installing Stash $VERSION for $os-$arch into $INSTALL_DIR"

# Asset names (match .github/workflows/release.yml).
cli_asset="stash-${os}-${arch}"
check_asset="stash-check-${os}-${arch}"
format_asset="stash-format-${os}-${arch}"
checksums_asset="checksums-sha256.txt"

base_url="https://github.com/$REPO/releases/download/$VERSION"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# Download.
for asset in "$cli_asset" "$check_asset" "$format_asset" "$checksums_asset"; do
    info "Downloading $asset..."
    if ! curl -fsSL -o "$tmp/$asset" "$base_url/$asset"; then
        err "failed to download $asset from $base_url. Is the release published and the asset name correct?"
    fi
done

# Verify SHA-256.
if [[ "${STASH_NO_VERIFY:-0}" == "1" ]]; then
    info "WARNING: skipping SHA-256 verification (STASH_NO_VERIFY=1)"
else
    info "Verifying SHA-256 checksums..."
    cd "$tmp"
    for asset in "$cli_asset" "$check_asset" "$format_asset"; do
        expected="$(grep -E "  $asset\$" "$checksums_asset" | awk '{print $1}')"
        [[ -z "$expected" ]] && err "no checksum found for $asset in $checksums_asset"

        if command -v sha256sum >/dev/null 2>&1; then
            actual="$(sha256sum "$asset" | awk '{print $1}')"
        elif command -v shasum >/dev/null 2>&1; then
            actual="$(shasum -a 256 "$asset" | awk '{print $1}')"
        else
            err "no sha256 utility found (need sha256sum or shasum)"
        fi

        if [[ "$actual" != "$expected" ]]; then
            err "checksum mismatch for $asset:\n  expected: $expected\n  actual:   $actual"
        fi
    done
    info "Checksums verified."
    cd - >/dev/null
fi

# Install.
mkdir -p "$INSTALL_DIR"
install -m 0755 "$tmp/$cli_asset"    "$INSTALL_DIR/stash"
install -m 0755 "$tmp/$check_asset"  "$INSTALL_DIR/stash-check"
install -m 0755 "$tmp/$format_asset" "$INSTALL_DIR/stash-format"

info "Installed:"
echo "    $INSTALL_DIR/stash"
echo "    $INSTALL_DIR/stash-check"
echo "    $INSTALL_DIR/stash-format"

# PATH guidance.
if ! echo ":$PATH:" | grep -q ":$INSTALL_DIR:"; then
    echo
    info "Add $INSTALL_DIR to your PATH. Append the following to your shell profile:"
    case "$(basename "${SHELL:-}")" in
        zsh)  echo "    echo 'export PATH=\"$INSTALL_DIR:\$PATH\"' >> ~/.zshrc";;
        fish) echo "    fish_add_path $INSTALL_DIR";;
        *)    echo "    echo 'export PATH=\"$INSTALL_DIR:\$PATH\"' >> ~/.bashrc";;
    esac
fi

echo
info "Done. Run 'stash --version' to verify."
