#!/usr/bin/env bash

set -euo pipefail

scriptroot="$(cd -P "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$scriptroot/../.." && pwd)"
subset="${1:-clr}"
configuration="${2:-Debug}"
output_dir="${3:-$repo_root/artifacts/obj/buildxl-dscript/$subset}"

mkdir -p "$output_dir"

"$repo_root/.dotnet/dotnet" run --project "$scriptroot/BuildXLGraphGenerator.csproj" -- \
  --repoRoot "$repo_root" \
  --subset "$subset" \
  --configuration "$configuration" \
  --outputDir "$output_dir"
