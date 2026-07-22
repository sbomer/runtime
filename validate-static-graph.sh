#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"

configuration="${CONFIGURATION:-Debug}"
runtime_configuration="${RUNTIME_CONFIGURATION:-Release}"
target_architecture="${TARGET_ARCHITECTURE:-x64}"
build_architecture="${BUILD_ARCHITECTURE:-x64}"
tasks_configuration="${TASKS_CONFIGURATION:-Debug}"
validation_tool_root="${MSBUILD_GRAPH_VALIDATION_ROOT:-$HOME/src/msbuild-graph-validation}"
target="${1:-${STATIC_GRAPH_TARGET:-clr}}"
subset="$target"
entry_project="Build.proj"
restore_label="$target"
use_project_reference_replay=false
dynamic_build_parallelism=()
capture_parallelism=()
static_graph_parallelism=()
max_static_graph_nodes=0
max_static_graph_edges=0
prerequisite_subsets=()
prerequisite_projects=()

case "$target" in
    clr)
        entry_project="Build.proj"
        restore_command=(./build.sh clr --restore --runtimeConfiguration "$runtime_configuration")
        ;;
    libs.native)
        entry_project="src/native/libs/build-native.proj"
        restore_command=(./dotnet.sh msbuild "$entry_project" /t:Restore "/p:Configuration=$configuration" "/p:TargetArchitecture=$target_architecture" "/p:BuildArchitecture=$build_architecture")
        ;;
    libs.sfx)
        entry_project="Build.proj"
        restore_command=(./build.sh libs.sfx --restore --runtimeConfiguration "$runtime_configuration")
        use_project_reference_replay=true
        dynamic_build_parallelism=(/m:1)
        capture_parallelism=(/m:2)
        static_graph_parallelism=(/m:2)
        max_static_graph_nodes=1000
        max_static_graph_edges=50000
        ;;
    libs.pretest)
        entry_project="Build.proj"
        prerequisite_subsets=("host.native+clr.runtime+clr.corelib+libs.sfx")
        restore_command=(./build.sh libs.pretest --restore --runtimeConfiguration "$runtime_configuration")
        use_project_reference_replay=true
        dynamic_build_parallelism=(/m:1)
        capture_parallelism=(/m:2)
        static_graph_parallelism=(/m:2)
        max_static_graph_nodes=1000
        max_static_graph_edges=50000
        ;;
    libs.tests)
        entry_project="Build.proj"
        prerequisite_subsets=("host.native+clr.runtime+clr.corelib+libs.sfx+libs.pretest")
        restore_command=(./build.sh libs.tests --restore --runtimeConfiguration "$runtime_configuration")
        use_project_reference_replay=true
        dynamic_build_parallelism=(/m:1)
        capture_parallelism=(/m:2)
        static_graph_parallelism=(/m:2)
        max_static_graph_nodes=10000
        max_static_graph_edges=500000
        ;;
    libs.oob)
        entry_project="src/libraries/oob.proj"
        prerequisite_projects=(
            "src/libraries/sfx-gen.proj"
            "src/libraries/sfx-src.proj"
            "src/libraries/sfx-finish.proj"
            "src/native/libs/build-native.proj"
        )
        restore_command=(./dotnet.sh msbuild "$entry_project" /t:Restore "/p:Configuration=$configuration" "/p:TargetArchitecture=$target_architecture" "/p:BuildArchitecture=$build_architecture")
        use_project_reference_replay=true
        dynamic_build_parallelism=(/m:1)
        capture_parallelism=(/m:2)
        static_graph_parallelism=(/m:2)
        max_static_graph_nodes=1000
        max_static_graph_edges=50000
        ;;
    *)
        echo "Unsupported static graph validation target '$target'. Supported targets: clr, libs.native, libs.sfx, libs.pretest, libs.tests, libs.oob" >&2
        exit 1
        ;;
esac

output_dir="$repo_root/artifacts/log/static-graph-validation/$target"
validation_output_dir="$repo_root/static-graph-validation"
diff_output_dir="$validation_output_dir/diffs"
nodes_output_dir="$validation_output_dir/nodes"
targets_output_dir="$validation_output_dir/targets/$target"
comparison_output_dir="$validation_output_dir/comparisons"
binlog_output_dir="$validation_output_dir/binlogs/$target"
diff_output="$diff_output_dir/static-graph-$target.diff"
dynamic_nodes_output="$nodes_output_dir/$target.dynamic.nodes.txt"
static_nodes_output="$nodes_output_dir/$target.static.nodes.txt"
comparison_output="$comparison_output_dir/static-graph-$target.comparison.txt"
work_dir="$(mktemp -d "${TMPDIR:-/tmp}/runtime-static-graph-validation.XXXXXX")"
prerequisite_artifacts_snapshot=""
reuse_dynamic_binlog="${REUSE_DYNAMIC_BINLOG:-}"

mkdir -p "$diff_output_dir" "$nodes_output_dir" "$comparison_output_dir" "$binlog_output_dir"

cleanup() {
    local exit_code="$1"

    if [[ -n "$prerequisite_artifacts_snapshot" && -d "$prerequisite_artifacts_snapshot" ]]; then
        if ((exit_code == 0)); then
            rm -rf -- "$prerequisite_artifacts_snapshot"
        else
            echo "Prerequisite artifact snapshot retained after failure: $prerequisite_artifacts_snapshot" >&2
        fi
    fi

    mkdir -p "$output_dir" "$binlog_output_dir"
    if ((exit_code == 0)); then
        find "$binlog_output_dir" -maxdepth 1 -type f -name '*.binlog' -delete
    fi
    while IFS= read -r -d '' binlog; do
        mv "$binlog" "$binlog_output_dir/"
    done < <(find "$work_dir" -maxdepth 1 -type f -name '*.binlog' -print0)
    cp -R "$work_dir"/. "$output_dir"/
    rm -rf "$work_dir"
}

trap 'cleanup $?' EXIT

if [[ -n "$reuse_dynamic_binlog" ]]; then
    if [[ "$reuse_dynamic_binlog" == "true" ]]; then
        reuse_dynamic_binlog="$binlog_output_dir/dynamic-$target.binlog"
    fi
    if [[ ! -f "$reuse_dynamic_binlog" ]]; then
        echo "Dynamic binlog to reuse does not exist: $reuse_dynamic_binlog" >&2
        exit 1
    fi
    cp "$reuse_dynamic_binlog" "$work_dir/dynamic-$target.binlog"
fi

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

    DOTNET_ROLL_FORWARD=Major ./dotnet.sh "$tool_dll" "$@"
}

msbuild_log_args() {
    local name="$1"
    echo "/flp:LogFile=$work_dir/$name.log;Verbosity=normal"
}

write_full_context_diff() {
    local left="$1"
    local right="$2"
    local left_label="$3"
    local right_label="$4"
    local output="$5"

    diff -u -U1000000 \
        --label "$left_label" \
        --label "$right_label" \
        "$left" \
        "$right" \
        > "$output" || true

    if [[ ! -s "$output" ]]; then
        {
            echo "--- $left_label"
            echo "+++ $right_label"
            local line_count
            line_count="$(wc -l < "$left")"
            echo "@@ -1,$line_count +1,$line_count @@"
            sed 's/^/ /' "$left"
        } > "$output"
    fi
}

clean_build_artifacts() {
    if [[ ! -d "$repo_root/artifacts" ]]; then
        return
    fi

    find "$repo_root/artifacts" -mindepth 1 -maxdepth 1 ! -name log -exec rm -rf -- {} +

    if [[ -d "$repo_root/artifacts/log" ]]; then
        find "$repo_root/artifacts/log" -mindepth 1 -maxdepth 1 ! -name static-graph-validation -exec rm -rf -- {} +
    fi
}

copy_build_artifacts() {
    local source_root="$1"
    local destination_root="$2"

    mkdir -p "$destination_root"
    while IFS= read -r -d '' artifact; do
        # A real copy keeps the snapshot isolated from writes performed by the dynamic build.
        cp -a "$artifact" "$destination_root/"
    done < <(find "$source_root" -mindepth 1 -maxdepth 1 ! -name log -print0)

    if [[ -d "$source_root/log" ]]; then
        mkdir -p "$destination_root/log"
        while IFS= read -r -d '' artifact_log; do
            cp -a "$artifact_log" "$destination_root/log/"
        done < <(find "$source_root/log" -mindepth 1 -maxdepth 1 ! -name static-graph-validation -print0)
    fi
}

move_build_artifacts() {
    local source_root="$1"
    local destination_root="$2"

    mkdir -p "$destination_root"
    while IFS= read -r -d '' artifact; do
        mv "$artifact" "$destination_root/"
    done < <(find "$source_root" -mindepth 1 -maxdepth 1 ! -name log -print0)

    if [[ -d "$source_root/log" ]]; then
        mkdir -p "$destination_root/log"
        while IFS= read -r -d '' artifact_log; do
            mv "$artifact_log" "$destination_root/log/"
        done < <(find "$source_root/log" -mindepth 1 -maxdepth 1 -print0)
    fi
}

snapshot_prerequisite_artifacts() {
    prerequisite_artifacts_snapshot="$(mktemp -d "$repo_root/.artifacts-prerequisite-snapshot.XXXXXX")"
    copy_build_artifacts "$repo_root/artifacts" "$prerequisite_artifacts_snapshot"
}

restore_prerequisite_artifacts() {
    clean_build_artifacts
    move_build_artifacts "$prerequisite_artifacts_snapshot" "$repo_root/artifacts"
    rm -rf -- "$prerequisite_artifacts_snapshot"
    prerequisite_artifacts_snapshot=""
}

common_properties=(
    "/p:Configuration=$configuration"
    "/p:RuntimeConfiguration=$runtime_configuration"
    "/p:Restore=false"
    "/p:Build=true"
    "/p:Ninja=true"
    "/p:TargetArchitecture=$target_architecture"
    "/p:BuildArchitecture=$build_architecture"
    "/p:FeatureDynamicCodeCompiled=true"
)

if [[ "$entry_project" == "Build.proj" ]]; then
    common_properties=("/p:Subset=$subset" "${common_properties[@]}")
fi

if [[ "$target" == "libs.sfx" ]]; then
    common_properties=("/p:ApiCompatValidateAssemblies=false" "${common_properties[@]}")
fi

build_prerequisites() {
    local phase="$1"

    if ((${#prerequisite_subsets[@]} != 0)); then
        log "Building ${prerequisite_subsets[*]} artifact prerequisites for the $phase build"
        run_timed "build $phase subset prerequisites" ./build.sh "${prerequisite_subsets[@]}" \
            -a "$target_architecture" \
            -c "$configuration" \
            -lc "$configuration" \
            -rc "$runtime_configuration" \
            "/p:BuildArchitecture=$build_architecture"
    fi

    if ((${#prerequisite_projects[@]} == 0)); then
        return
    fi

    local prerequisite_project
    local prerequisite_name
    for prerequisite_project in "${prerequisite_projects[@]}"; do
        prerequisite_name="$(basename "$prerequisite_project")"

        log "Restoring $prerequisite_project for the $phase build"
        run_timed "restore $phase prerequisite $prerequisite_name" ./dotnet.sh msbuild "$prerequisite_project" \
            /t:Restore \
            /nr:false \
            "$(msbuild_log_args "${phase}-${prerequisite_name}-prerequisite-restore")" \
            "/p:Configuration=$configuration" \
            "/p:TargetArchitecture=$target_architecture" \
            "/p:BuildArchitecture=$build_architecture"

        log "Building $prerequisite_project for the $phase build"
        run_timed "build $phase prerequisite $prerequisite_name" ./dotnet.sh msbuild "$prerequisite_project" \
            /nr:false \
            "${dynamic_build_parallelism[@]}" \
            "$(msbuild_log_args "${phase}-${prerequisite_name}-prerequisite-build")" \
            "${common_properties[@]}"
    done
}

log "Removing artifacts for a clean validation run"
clean_build_artifacts

log "Restoring $restore_label"
run_timed "restore dynamic" "${restore_command[@]}" "$(msbuild_log_args restore-dynamic)"

build_prerequisites dynamic

if ((${#prerequisite_subsets[@]} != 0 || ${#prerequisite_projects[@]} != 0)); then
    log "Snapshotting prerequisite artifacts for the isolated static graph build"
    run_timed "snapshot prerequisite artifacts" snapshot_prerequisite_artifacts
fi

static_graph_properties=()
if [[ "$use_project_reference_replay" == "true" ]]; then
    capture_file="$work_dir/project-references.raw.txt"
    replay_file="$targets_output_dir/project-references.replay.targets"
    mkdir -p "$targets_output_dir"
    static_graph_tasks="$repo_root/artifacts/bin/StaticGraphTasks/$tasks_configuration/net11.0/StaticGraphTasks.dll"
    replay_generator_tasks="$work_dir/StaticGraphTasks.dll"

    log "Building project reference capture and replay tasks"
    run_timed "build replay tasks" ./dotnet.sh build src/tasks/StaticGraphTasks/StaticGraphTasks.csproj \
        --configuration "$tasks_configuration" \
        --nologo \
        --verbosity:minimal

    log "Capturing the negotiated dynamic project graph"
    run_timed "capture dynamic graph" ./dotnet.sh msbuild "$entry_project" \
        /t:CaptureProjectReferencesRecursive \
        /nr:false \
        "${capture_parallelism[@]}" \
        "/bl:$work_dir/capture-$target.binlog" \
        "$(msbuild_log_args capture-dynamic-graph)" \
        "/p:CaptureProjectReferences=true" \
        "/p:CaptureProjectReferencesFile=$capture_file" \
        "/p:StaticGraphTasksAssemblyPath=$static_graph_tasks" \
        "${common_properties[@]}"

    cp "$static_graph_tasks" "$replay_generator_tasks"

    static_graph_properties=(
        "/p:UseProjectReferenceReplay=true"
        "/p:ProjectReferenceReplayTargets=$replay_file"
    )
fi

if [[ -n "$reuse_dynamic_binlog" ]]; then
    log "Reusing dynamic non-graph $target build binlog from $reuse_dynamic_binlog"
else
    log "Capturing dynamic non-graph $target build"
    run_timed "dynamic build" ./dotnet.sh msbuild "$entry_project" \
        /nr:false \
        "${dynamic_build_parallelism[@]}" \
        "/bl:$work_dir/dynamic-$target.binlog" \
        "$(msbuild_log_args dynamic-build)" \
        "${common_properties[@]}"
fi

log "Removing artifacts before the isolated static graph build"
clean_build_artifacts

if [[ "$use_project_reference_replay" == "true" ]]; then
    mkdir -p "$output_dir"
    log "Generating project reference replay targets"
    run_timed "generate project reference replay" ./dotnet.sh msbuild eng/projectReferenceReplay.targets \
        /t:GenerateProjectReferenceReplay \
        /nr:false \
        "$(msbuild_log_args generate-project-reference-replay)" \
        "/p:CaptureProjectReferencesFile=$capture_file" \
        "/p:ProjectReferenceReplayTargets=$replay_file" \
        "/p:StaticGraphTasksAssemblyPath=$replay_generator_tasks"
    rm "$replay_generator_tasks"
fi

if [[ -n "$prerequisite_artifacts_snapshot" ]]; then
    log "Restoring prerequisite artifacts for the isolated static graph build"
    run_timed "restore prerequisite artifacts" restore_prerequisite_artifacts
else
    log "Restoring $restore_label for the isolated static graph build"
    run_timed "restore static" "${restore_command[@]}" "$(msbuild_log_args restore-static)"
    build_prerequisites static
fi

log "Running isolated static graph $target build"
run_timed "static graph build" ./dotnet.sh msbuild "$entry_project" \
    /graphBuild \
    /isolateProjects \
    /nr:false \
    "${static_graph_parallelism[@]}" \
    "/bl:$work_dir/static-$target.binlog" \
    "$(msbuild_log_args static-build)" \
    "${static_graph_properties[@]}" \
    "${common_properties[@]}"

if ((max_static_graph_nodes > 0 || max_static_graph_edges > 0)); then
    static_graph_nodes="$(sed -n 's/.*Static graph loaded.*: \([0-9][0-9]*\) nodes.*/\1/p' "$work_dir/static-build.log" | head -n 1)"
    static_graph_edges="$(sed -n 's/.*Static graph loaded.*: [0-9][0-9]* nodes, \([0-9][0-9]*\) edges.*/\1/p' "$work_dir/static-build.log" | head -n 1)"
    if [[ -z "$static_graph_nodes" || -z "$static_graph_edges" ]]; then
        echo "Unable to determine the static graph size." >&2
        exit 1
    fi
    if ((max_static_graph_nodes > 0 && static_graph_nodes > max_static_graph_nodes)); then
        echo "Static graph contains $static_graph_nodes nodes, exceeding the limit of $max_static_graph_nodes." >&2
        exit 1
    fi
    if ((max_static_graph_edges > 0 && static_graph_edges > max_static_graph_edges)); then
        echo "Static graph contains $static_graph_edges edges, exceeding the limit of $max_static_graph_edges." >&2
        exit 1
    fi
    echo "Static graph size: $static_graph_nodes nodes / $static_graph_edges edges (limits: $max_static_graph_nodes / $max_static_graph_edges)"
fi

log "Comparing dynamic and static graph binlogs"
run_graph_validation_tool dynamic "$work_dir/dynamic-$target.binlog" \
    --repo-root "$repo_root" \
    -o "$work_dir/dynamic.json"

static_capture_properties=()
for property in "${static_graph_properties[@]}" "${common_properties[@]}"; do
    static_capture_properties+=(-p "${property#/p:}")
done

run_graph_validation_tool static "$entry_project" \
    --repo-root "$repo_root" \
    -t Build \
    "${static_capture_properties[@]}" \
    -o "$work_dir/static.json"

run_graph_validation_tool normalize "$work_dir/dynamic.json" \
    --output-dir "$work_dir/normalized/dynamic"

run_graph_validation_tool normalize "$work_dir/static.json" \
    --output-dir "$work_dir/normalized/static"

for file in nodes.txt edges.txt; do
    sed -i "s|$repo_root/||g" "$work_dir/normalized/dynamic/$file" "$work_dir/normalized/static/$file"
done

cp "$work_dir/normalized/dynamic/nodes.txt" "$dynamic_nodes_output"
cp "$work_dir/normalized/static/nodes.txt" "$static_nodes_output"

write_full_context_diff \
    "$dynamic_nodes_output" \
    "$static_nodes_output" \
    "dynamic/nodes.txt" \
    "static/nodes.txt" \
    "$diff_output"

cp "$diff_output" "$work_dir/static-graph-$target.diff"

comparison_exit_code=0
run_graph_validation_tool compare \
    --left "$work_dir/dynamic.json" \
    --right "$work_dir/static.json" \
    --repo-root "$repo_root" \
    -o "$work_dir/comparison.txt" || comparison_exit_code=$?

if ((comparison_exit_code != 0 && comparison_exit_code != 2)); then
    exit "$comparison_exit_code"
fi

cp "$work_dir/comparison.txt" "$comparison_output"

echo
if ((comparison_exit_code == 0)); then
    echo "Static graph binlog comparison passed"
else
    echo "Static graph binlog comparison completed with differences"
fi
echo "Artifacts copied to $output_dir"
echo "Binlogs copied to $binlog_output_dir"
echo "Diff written to $diff_output"
echo "Node files written to $dynamic_nodes_output and $static_nodes_output"
echo "Comparison report written to $comparison_output"
