#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"

configuration="${CONFIGURATION:-Debug}"
runtime_configuration="${RUNTIME_CONFIGURATION:-Release}"
target_architecture="${TARGET_ARCHITECTURE:-x64}"
build_architecture="${BUILD_ARCHITECTURE:-x64}"
validation_tool_root="${MSBUILD_GRAPH_VALIDATION_ROOT:-$HOME/src/msbuild-graph-validation}"
subset="clr"
output_dir="$repo_root/artifacts/log/static-graph-validation/$subset"
diff_output="$repo_root/static-graph-$subset.diff"
work_dir="$(mktemp -d "${TMPDIR:-/tmp}/runtime-static-graph-validation.XXXXXX")"

cleanup() {
    mkdir -p "$output_dir"
    cp -R "$work_dir"/. "$output_dir"/
    rm -rf "$work_dir"
}

trap cleanup EXIT

log() {
    echo
    echo "==> $*"
}

run_timed() {
    local label="$1"
    shift

    local start
    local end
    start="$(date +%s)"
    "$@"
    end="$(date +%s)"

    local elapsed=$((end - start))
    printf '%s: %02d:%02d:%02d\n' "$label" $((elapsed / 3600)) $(((elapsed % 3600) / 60)) $((elapsed % 60)) | tee -a "$work_dir/timings.txt"
}

run_graph_validation_tool() {
    local tool_dll="$validation_tool_root/artifacts/bin/StaticGraphValidation/debug/StaticGraphValidation.dll"

    if [[ ! -f "$tool_dll" ]]; then
        dotnet build "$validation_tool_root/msbuild-graph-validation.slnx" -v:minimal
    fi

    DOTNET_ROLL_FORWARD=Major dotnet "$tool_dll" "$@"
}

common_properties=(
    "/p:Subset=$subset"
    "/p:Configuration=$configuration"
    "/p:RuntimeConfiguration=$runtime_configuration"
    "/p:Restore=false"
    "/p:Build=true"
    "/p:Ninja=true"
    "/p:TargetArchitecture=$target_architecture"
    "/p:BuildArchitecture=$build_architecture"
    "/p:FeatureDynamicCodeCompiled=true"
)

log "Removing artifacts for a clean validation run"
rm -rf "$repo_root/artifacts"

log "Restoring the clr subset"
run_timed "restore dynamic" ./build.sh clr --restore --runtimeConfiguration "$runtime_configuration"

log "Capturing dynamic non-graph clr build"
run_timed "dynamic build" ./dotnet.sh msbuild Build.proj \
    "/bl:$work_dir/dynamic-clr.binlog" \
    "${common_properties[@]}"

log "Removing artifacts before the isolated static graph build"
rm -rf "$repo_root/artifacts"

log "Restoring the clr subset for the isolated static graph build"
run_timed "restore static" ./build.sh clr --restore --runtimeConfiguration "$runtime_configuration"

log "Running isolated static graph clr build"
run_timed "static graph build" ./dotnet.sh msbuild Build.proj \
    /graphBuild \
    /isolateProjects \
    "/bl:$work_dir/static-clr.binlog" \
    "${common_properties[@]}"

log "Comparing dynamic and static graph binlogs"
run_graph_validation_tool dynamic "$work_dir/dynamic-clr.binlog" \
    --repo-root "$repo_root" \
    -o "$work_dir/dynamic.json"

run_graph_validation_tool dynamic "$work_dir/static-clr.binlog" \
    --repo-root "$repo_root" \
    -o "$work_dir/static.json"

run_graph_validation_tool normalize "$work_dir/dynamic.json" \
    --output-dir "$work_dir/normalized/dynamic"

run_graph_validation_tool normalize "$work_dir/static.json" \
    --output-dir "$work_dir/normalized/static"

for file in nodes.txt edges.txt; do
    sed -i "s|$repo_root/||g" "$work_dir/normalized/dynamic/$file" "$work_dir/normalized/static/$file"
done

diff -u -U1000000 \
    --label "dynamic/nodes.txt" \
    --label "static/nodes.txt" \
    "$work_dir/normalized/dynamic/nodes.txt" \
    "$work_dir/normalized/static/nodes.txt" \
    > "$diff_output" || true

cp "$diff_output" "$work_dir/static-graph-$subset.diff"

run_graph_validation_tool compare \
    --left "$work_dir/dynamic.json" \
    --right "$work_dir/static.json" \
    --repo-root "$repo_root" \
    -o "$work_dir/comparison.txt"

echo
echo "Static graph binlog comparison passed"
echo "Artifacts copied to $output_dir"
echo "Diff written to $diff_output"
