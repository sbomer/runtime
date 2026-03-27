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
echo "=== Restoring NuGet packages ==="
"$SCRIPT_DIR/build.sh" --restore --subset libs.oob

echo "=== Running BuildXL ==="
export DOTNET_HOST_PATH="$HOME/.dotnet/dotnet"
exec "$BXL" \
    /c:"$SCRIPT_DIR/config.dsc" \
    /EnableLinuxEBPFSandbox- \
    "$@"
