#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"

./dotnet.sh build \
    src/tools/StaticGraphDriver/StaticGraphDriver.csproj \
    --configuration Debug \
    --nologo \
    --verbosity:quiet

driver="$(find artifacts/bin/StaticGraphDriver/Debug -name StaticGraphDriver.dll -print -quit 2>/dev/null || true)"
if [[ -z "$driver" ]]; then
    echo "Unable to find the built StaticGraphDriver." >&2
    exit 1
fi

exec ./.dotnet/dotnet "$driver" "$@"
