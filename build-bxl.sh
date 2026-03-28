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

# Parse our own flags and separate them from bxl pass-through args.
BXL_ARGS=()
for arg in "$@"; do
    case "$arg" in
        *) BXL_ARGS+=("$arg") ;;
    esac
done

# Phase 1: NuGet restore via BuildXL.
# Runs dotnet restore as a cached pip. BuildXL tracks all source file reads
# (csproj, props, targets, NuGet.config, global.json) so the pip only
# re-executes when inputs actually change. Cache hit: ~0s vs ~18s.
echo "=== Phase 1: NuGet restore ==="
export DOTNET_HOST_PATH="$HOME/.dotnet/dotnet"
"$BXL" \
    /c:"$SCRIPT_DIR/config.restore.dsc" \
    /EnableLinuxEBPFSandbox- \
    /server- \
    /cacheGraph-

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

# Phase 2: Build (MSBuild + Ninja resolvers).
echo "=== Phase 2: Build ==="
exec "$BXL" \
    /c:"$SCRIPT_DIR/config.dsc" \
    /EnableLinuxEBPFSandbox- \
    "${BXL_ARGS[@]+"${BXL_ARGS[@]}"}"
