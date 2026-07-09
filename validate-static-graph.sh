#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"

configuration="${CONFIGURATION:-Debug}"
runtime_configuration="${RUNTIME_CONFIGURATION:-Release}"
target_architecture="${TARGET_ARCHITECTURE:-x64}"
build_architecture="${BUILD_ARCHITECTURE:-x64}"
output_dir="$repo_root/artifacts/log/static-graph-validation/clr"
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

run_validation_tool() {
    ./dotnet.sh run \
        --project src/tools/StaticGraphValidation/StaticGraphValidation.csproj \
        -c "$configuration" \
        --no-build \
        -- "$@"
}

common_properties=(
    "/p:Subset=clr"
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
./build.sh clr --restore --runtimeConfiguration "$runtime_configuration"

log "Building validation tools"
./dotnet.sh build src/tools/StaticGraphValidation/StaticGraphValidation.csproj -c "$configuration"
./dotnet.sh build src/tasks/StaticGraphTasks/StaticGraphTasks.csproj -c "$configuration" --no-restore

log "Capturing dynamic non-graph clr build"
./dotnet.sh msbuild Build.proj \
    "/bl:$work_dir/dynamic-clr.binlog" \
    "${common_properties[@]}"

run_validation_tool dynamic "$work_dir/dynamic-clr.binlog" \
    --repo-root "$repo_root" \
    -o "$work_dir/dynamic.json"

log "Capturing selected target frameworks"
./dotnet.sh msbuild Build.proj \
    /t:CaptureStaticGraphSelectedTargetFrameworksRecursive \
    "/bl:$work_dir/phase1-clr.binlog" \
    "${common_properties[@]}" \
    /p:CaptureStaticGraphSelectedTargetFrameworks=true \
    "/p:StaticGraphSelectedTargetFrameworksRawFile=$work_dir/selected.raw.txt"

./dotnet.sh msbuild Build.proj \
    /t:GenerateStaticGraphSelectedTargetFrameworks \
    "/p:Configuration=$configuration" \
    "/p:RuntimeConfiguration=$runtime_configuration" \
    /p:Ninja=true \
    "/p:TargetArchitecture=$target_architecture" \
    "/p:BuildArchitecture=$build_architecture" \
    /p:FeatureDynamicCodeCompiled=true \
    "/p:StaticGraphSelectedTargetFrameworksRawFile=$work_dir/selected.raw.txt" \
    "/p:StaticGraphSelectedTargetFrameworksTargets=$work_dir/selected.targets"

run_validation_tool dynamic "$work_dir/phase1-clr.binlog" \
    --repo-root "$repo_root" \
    -o "$work_dir/phase1.json"

log "Removing artifacts before the isolated static graph build"
rm -rf "$repo_root/artifacts"

log "Restoring the clr subset for the isolated static graph build"
./build.sh clr --restore --runtimeConfiguration "$runtime_configuration"

log "Running isolated static graph clr build"
./dotnet.sh msbuild Build.proj \
    /graphBuild \
    /isolateProjects \
    "/bl:$work_dir/static-clr.binlog" \
    "${common_properties[@]}" \
    /p:StaticGraphUseSelectedTargetFrameworks=true \
    "/p:StaticGraphSelectedTargetFrameworksTargets=$work_dir/selected.targets"

log "Building validation tool for comparison reports"
./dotnet.sh build src/tools/StaticGraphValidation/StaticGraphValidation.csproj -c "$configuration"

run_validation_tool static Build.proj \
    --repo-root "$repo_root" \
    -o "$work_dir/static.json" \
    -p Subset=clr \
    -p "Configuration=$configuration" \
    -p "RuntimeConfiguration=$runtime_configuration" \
    -p Restore=false \
    -p Build=true \
    -p Ninja=true \
    -p "TargetArchitecture=$target_architecture" \
    -p "BuildArchitecture=$build_architecture" \
    -p FeatureDynamicCodeCompiled=true \
    -p StaticGraphUseSelectedTargetFrameworks=true \
    -p "StaticGraphSelectedTargetFrameworksTargets=$work_dir/selected.targets"

log "Comparing dynamic, phase1, and static replay graphs"
run_validation_tool compare-replay \
    --dynamic "$work_dir/dynamic.json" \
    --phase1 "$work_dir/phase1.json" \
    --static "$work_dir/static.json" \
    -o "$work_dir/comparison.txt" || true

run_validation_tool compare-replay \
    --dynamic "$work_dir/dynamic.json" \
    --phase1 "$work_dir/phase1.json" \
    --static "$work_dir/static.json" \
    --format json \
    -o "$work_dir/comparison.json" || true

python - "$work_dir/comparison.json" <<'PY'
import json
import sys

comparison_path = sys.argv[1]
with open(comparison_path, encoding="utf-8") as file:
    comparison = json.load(file)

print()
print("Static graph replay node comparison")
print(f"  dynamic query nodes: {comparison['DynamicQueryCount']}")
print(f"  phase1 query nodes: {comparison['Phase1QueryCount']}")
print(f"  dynamic build nodes: {comparison['DynamicNonQueryCount']}")
print(f"  static replay build nodes: {comparison['StaticReplayCount']}")

checks = {
    "query nodes only in dynamic build": comparison["QueryDynamicOnly"],
    "query nodes only in phase1 capture": comparison["QueryPhase1Only"],
    "build nodes only in dynamic build": comparison["BuildPresenceDynamicOnly"],
    "build nodes only in static replay": comparison["BuildPresenceStaticOnly"],
    "query edges only in dynamic build": comparison["QueryEdgeDynamicOnly"],
    "query edges only in phase1 capture": comparison["QueryEdgePhase1Only"],
}

failed = False
for name, entries in checks.items():
    if entries:
        failed = True
        print()
        print(f"{name}: {len(entries)}")
        for entry in entries[:20]:
            print(f"  {entry.get('DisplayName', entry)}")
        if len(entries) > 20:
            print(f"  ... {len(entries) - 20} more")

if failed:
    print()
    print(f"Full comparison report: {comparison_path[:-5]}.txt")
    sys.exit(1)

print("  node comparison passed")
print(f"Full comparison report: {comparison_path[:-5]}.txt")
PY
