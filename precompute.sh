#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -P "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
harness_run="$script_dir/../msbuild-precompute-harness/run.sh"

cd "$script_dir"
exec "$harness_run" "$@"
