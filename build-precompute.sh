#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -P "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
arcade_build="$script_dir/artifacts/toolset/11.0.0-beta.26411.119/Build.proj"
precompute_msbuild="/home/sven/msbr/develop/msbuild/artifacts/bin/bootstrap/core/sdk/11.0.100-dev/MSBuild.dll"
work_root="/home/sven/msbt/manual-precompute/$(basename "$script_dir")"
package_changes="$script_dir/precompute-package-changes"
git_dir="$(git -C "$script_dir" rev-parse --path-format=absolute --git-dir)"
git_common_dir="$(git -C "$script_dir" rev-parse --path-format=absolute --git-common-dir)"

export NUGET_PACKAGES="$work_root/nuget-packages"
mkdir -p "$NUGET_PACKAGES"

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

cp -a "$package_changes/." "$NUGET_PACKAGES/"

PRECOMPUTE_WORK_DIR="$work_root/build" \
PRECOMPUTE_META_SOURCE_DIRECTORIES="$script_dir/artifacts/obj;$git_dir;$git_common_dir" \
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
