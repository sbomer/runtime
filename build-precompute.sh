#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -P "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
arcade_build="$script_dir/artifacts/toolset/11.0.0-beta.26411.119/Build.proj"
work_root="/home/sven/msbt/manual-precompute/$(basename "$script_dir")"
package_changes="$script_dir/precompute-package-changes"
bootstrap_changes="$script_dir/precompute-bootstrap-changes"
git_dir="$(git -C "$script_dir" rev-parse --path-format=absolute --git-dir)"
git_common_dir="$(git -C "$script_dir" rev-parse --path-format=absolute --git-common-dir)"

toolchain_source="${PRECOMPUTE_TOOLCHAIN_SOURCE:-/home/sven/msbr/develop/msbuild/artifacts/bin/bootstrap/core}"
toolchain_root="$work_root/toolchain/core"
rm -rf "$toolchain_root"
mkdir -p "$toolchain_root"
cp -a "$toolchain_source/." "$toolchain_root/"

apply_bootstrap_patch() {
  local target="$1"
  local patch="$2"
  # Bootstrap SDK files may be CRLF (with BOM). Ignore whitespace so the
  # repo-owned patches still apply to those files.
  if git apply --check --ignore-whitespace --unsafe-paths --directory="$(dirname "$target")" "$patch"; then
    git apply --ignore-whitespace --unsafe-paths --directory="$(dirname "$target")" "$patch"
  elif ! git apply --reverse --check --ignore-whitespace --unsafe-paths --directory="$(dirname "$target")" "$patch"; then
    echo "Unable to apply bootstrap patch $patch to $target." >&2
    exit 1
  fi
}

git_targets="$toolchain_root/sdk/11.0.100-dev/Sdks/Microsoft.Build.Tasks.Git/build/Microsoft.Build.Tasks.Git.targets"
apply_bootstrap_patch "$git_targets" "$bootstrap_changes/Microsoft.Build.Tasks.Git.targets.patch"

publish_targets="$toolchain_root/sdk/11.0.100-dev/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Publish.targets"
for publish_patch in "$bootstrap_changes"/*-publish-*.patch; do
  apply_bootstrap_patch "$publish_targets" "$publish_patch"
done

crossgen_targets="$toolchain_root/sdk/11.0.100-dev/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.CrossGen.targets"
apply_bootstrap_patch "$crossgen_targets" "$bootstrap_changes/75bfc5dec2-readytorun.patch"

api_compat_targets="$toolchain_root/sdk/11.0.100-dev/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.ApiCompat.ValidatePackage.targets"
apply_bootstrap_patch "$api_compat_targets" "$bootstrap_changes/Microsoft.NET.ApiCompat.ValidatePackage.targets.patch"

export PRECOMPUTE_TOOLCHAIN_ROOT="$toolchain_root"
precompute_msbuild="$toolchain_root/sdk/11.0.100-dev/MSBuild.dll"

export NUGET_PACKAGES="$work_root/nuget-packages"
mkdir -p "$NUGET_PACKAGES"
rm -f \
  "$script_dir/artifacts/bin/Crossgen2Tasks/Debug/Microsoft.NET.CrossGen.props" \
  "$script_dir/artifacts/bin/Crossgen2Tasks/Debug/Microsoft.NET.CrossGen.targets"

"$script_dir/.dotnet/dotnet" restore \
  "$script_dir/eng/precompute-restore-prerequisites.proj" \
  --packages "$NUGET_PACKAGES" \
  --configfile "$script_dir/NuGet.config" \
  --no-http-cache \
  "/p:NuGetAudit=false"

PRECOMPUTE_WORK_DIR="$work_root/restore" \
PRECOMPUTE_ADDITIONAL_WRITABLE_DIRECTORIES="$NUGET_PACKAGES" \
PRECOMPUTE_ADDITIONAL_UNTRACKED_SCOPES="$NUGET_PACKAGES" \
"$script_dir/precompute.sh" \
  -- \
  "$script_dir/Build.proj" \
  "-t:Restore" \
  "/p:DisableCrossgen2SdkOverride=true" \
  "/p:EmbedProjectAssetsFile=false" \
  "$@"

"$work_root/restore/DriverState/dotnet" \
  "$precompute_msbuild" \
  "$script_dir/src/libraries/externals.csproj" \
  "-t:Restore" \
  "/p:RestorePackagesPath=$NUGET_PACKAGES" \
  "/p:RestoreConfigFile=$script_dir/NuGet.config" \
  "/p:NuGetAudit=false" \
  "/p:EmbedProjectAssetsFile=false" \
  "$@"

"$work_root/restore/DriverState/dotnet" \
  "$precompute_msbuild" \
  "$script_dir/src/tasks/Crossgen2Tasks/Crossgen2Tasks.csproj" \
  "-t:Build" \
  "/p:Configuration=Debug" \
  "/p:Restore=false" \
  "/p:EmbedProjectAssetsFile=false" \
  "/p:EnableSourceLink=false" \
  "$@"

cp -a "$package_changes/." "$NUGET_PACKAGES/"

nuget_pack_targets="$(dirname "$precompute_msbuild")/NuGet.Build.Tasks.Pack.targets"
python3 - "$nuget_pack_targets" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
text = path.read_text()
changed = False
original = '    <PackTask PackItem="$(PackProjectInputFile)"'
previous = '    <PackTask PrecomputeInputs="@(NuGetPackInput);$(ProjectAssetsFile)"\n              PackItem="$(PackProjectInputFile)"'
previous_explicit_outputs = (
    '    <PackTask PrecomputeInputs="@(NuGetPackInput);$(ProjectAssetsFile);'
    '$(BaseOutputPath)$(Configuration)/$(TargetFrameworks.Replace(\';\', '
    '\'/$(AssemblyName).pdb;$(BaseOutputPath)$(Configuration)/\'))/$(AssemblyName).pdb;'
    '$(BaseOutputPath)$(Configuration)/$(TargetFrameworks.Replace(\';\', '
    '\'/$(AssemblyName).xml;$(BaseOutputPath)$(Configuration)/\'))/$(AssemblyName).xml"\n'
    '              PackItem="$(PackProjectInputFile)"'
)
previous_output_directories = (
    '    <PackTask PrecomputeInputs="@(NuGetPackInput);$(ProjectAssetsFile);'
    '$(BaseOutputPath)$(Configuration)/$(TargetFrameworks.Replace(\';\', '
    '\'#dir;$(BaseOutputPath)$(Configuration)/\'))#dir"\n'
    '              PackItem="$(PackProjectInputFile)"'
)
replacement = (
    '    <PackTask PrecomputeInputs="@(NuGetPackInput);$(ProjectAssetsFile);'
    '@(_PrecomputePackTargetFramework->\'$(BaseOutputPath)$(Configuration)/%(Identity)#dir\')"\n'
    '              PackItem="$(PackProjectInputFile)"'
)
pack_task_inputs = '              PrecomputeInputs="@(_BuildOutputInPackage);@(NuGetPackInput)"'
pack_task_inputs_replacement = (
    '              PrecomputeInputs="@(_BuildOutputInPackage);@(NuGetPackInput);'
    '$(ProjectAssetsFile);@(_PrecomputePackTargetFramework->'
    '\'$(BaseOutputPath)$(Configuration)/%(Identity)#dir\')"'
)
if pack_task_inputs_replacement in text:
    pass
elif pack_task_inputs in text:
    text = text.replace(pack_task_inputs, pack_task_inputs_replacement, 1)
    changed = True
elif replacement not in text:
    if previous_output_directories in text:
        text = text.replace(previous_output_directories, replacement, 1)
    elif previous_explicit_outputs in text:
        text = text.replace(previous_explicit_outputs, replacement, 1)
    elif previous in text:
        text = text.replace(previous, replacement, 1)
    elif original in text:
        text = text.replace(original, replacement, 1)
    else:
        raise SystemExit(f"Unable to patch {path}: PackTask invocation not found")
    changed = True

project_reference_versions = (
    '  <Target Name="_GetProjectReferenceVersions"\n'
    '          Condition="\'$(NuspecFile)\' == \'\'"\n'
    '          DependsOnTargets="_GetAbsoluteOutputPathsForPack;$(GetPackageVersionDependsOn)">'
)
project_reference_versions_replacement = (
    '  <Target Name="_GetProjectReferenceVersions"\n'
    '          Condition="\'$(NuspecFile)\' == \'\'"\n'
    '          PrecomputeInputs="$(ProjectAssetsFile)"\n'
    '          DependsOnTargets="_GetAbsoluteOutputPathsForPack;$(GetPackageVersionDependsOn)">'
)
if project_reference_versions_replacement not in text:
    if project_reference_versions not in text:
        raise SystemExit(f"Unable to patch {path}: _GetProjectReferenceVersions target not found")
    text = text.replace(
        project_reference_versions,
        project_reference_versions_replacement,
        1)
    changed = True

if changed:
    path.write_text(text)
PY

framework_lists=("$script_dir"/.dotnet/packs/Microsoft.NETCore.App.Ref/*/data/FrameworkList.xml)
if [[ ! -f "${framework_lists[0]}" ]]; then
  echo "Unable to locate a Microsoft.NETCore.App.Ref FrameworkList.xml in $script_dir/.dotnet/packs." >&2
  exit 1
fi
framework_list_source="$(printf '%s\n' "${framework_lists[@]}" | sort -V | tail -n 1)"
framework_list_destination="$script_dir/artifacts/bin/microsoft.netcore.app.ref/data/FrameworkList.xml"
mkdir -p "$(dirname "$framework_list_destination")"
cp "$framework_list_source" "$framework_list_destination"

arcade_precompute_import='  <Import Project="$(RepoRoot)eng/precompute/ArcadeBuild.targets" Condition="'"'"'$(IsPrecomputePhase)'"'"' == '"'"'true'"'"'" />'
if ! grep -Fq 'eng/precompute/ArcadeBuild.targets' "$arcade_build"; then
  sed -i "\|</Project>|i\\$arcade_precompute_import" "$arcade_build"
fi

PRECOMPUTE_WORK_DIR="$work_root/build" \
PRECOMPUTE_META_SOURCE_DIRECTORIES="$script_dir/artifacts/obj;$script_dir/artifacts/bin/Crossgen2Tasks/Debug;$script_dir/artifacts/bin/microsoft.netcore.app.ref/data;$git_dir;$git_common_dir" \
exec "$script_dir/precompute.sh" \
  -- \
  "$arcade_build" \
  "/p:Configuration=Debug" \
  "/p:RepoRoot=$script_dir/" \
  "/p:Restore=false" \
  "/p:Build=true" \
  "/p:EmbedProjectAssetsFile=false" \
  "/p:EnableSourceLink=false" \
  "/p:GitRepositoryConfigurationScope=local" \
  "$@"
