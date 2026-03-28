#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BXL_DIR="$HOME/src/BuildXL"
BXL="$BXL_DIR/Out/Selfhost/Dev/bxl"

if [[ ! -x "$BXL" ]]; then
    echo "Error: locally-built bxl not found at $BXL" >&2
    echo "Build it first: cd $BXL_DIR && ./bxl.sh --deploy-dev --minimal /EnableLinuxEBPFSandbox-" >&2
    exit 1
fi

# Restore NuGet packages outside BuildXL (idempotent after first run).
# OOB libraries have NuGet PackageReferences for downlevel TFMs.
# Skip if already done — dotnet restore takes ~18s even when up-to-date.
# Pass --restore to force re-running (e.g., after adding new PackageReferences).
# Parse our own flags and separate them from bxl pass-through args.
FORCE_RESTORE=false
BXL_ARGS=()
for arg in "$@"; do
    case "$arg" in
        --restore) FORCE_RESTORE=true ;;
        *) BXL_ARGS+=("$arg") ;;
    esac
done

RESTORE_STAMP="$SCRIPT_DIR/artifacts/obj/.bxl-restore-done"
if [[ ! -f "$RESTORE_STAMP" ]] || $FORCE_RESTORE; then
    echo "=== Restoring NuGet packages ==="
    "$SCRIPT_DIR/build.sh" --restore --subset libs.oob
    touch "$RESTORE_STAMP"
else
    echo "=== NuGet packages already restored (pass --restore to force) ==="
fi

# Generate Ninja build files for native C libraries via CMake.
# CMake probes the system (compiler, headers, libraries) and generates build.ninja
# which the Ninja resolver reads at graph construction time.
# Idempotent: re-running on an existing build dir produces identical output.
# Skip if build.ninja already exists — CMake takes ~0.5s but the output is stable.
NATIVE_BUILD_DIR="$SCRIPT_DIR/artifacts/obj/native/linux-x64-Debug"
NATIVE_BIN_DIR="$NATIVE_BUILD_DIR/bin"
if [[ ! -f "$NATIVE_BUILD_DIR/build.ninja" ]]; then
    echo "=== Generating native build files (CMake) ==="
    cmake --no-warn-unused-cli -G Ninja \
        -DCMAKE_BUILD_TYPE=Debug \
        -DCMAKE_INSTALL_PREFIX="$NATIVE_BIN_DIR" \
        -DFEATURE_DISTRO_AGNOSTIC_SSL=1 \
        -DCMAKE_STATIC_LIB_LINK=0 \
        -S "$SCRIPT_DIR/src/native/libs" \
        -B "$NATIVE_BUILD_DIR"
else
    echo "=== Native build files already generated ==="
fi

echo "=== Running BuildXL ==="
export DOTNET_HOST_PATH="$HOME/.dotnet/dotnet"
exec "$BXL" \
    /c:"$SCRIPT_DIR/config.dsc" \
    /EnableLinuxEBPFSandbox- \
    "${BXL_ARGS[@]+"${BXL_ARGS[@]}"}"
