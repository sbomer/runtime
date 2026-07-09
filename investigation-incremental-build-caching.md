# Investigation: Incremental Build Caching for dotnet/runtime

## Goal

Enable `./build.sh clr.aot` (and eventually other subsets) to reuse cached
build artifacts across worktrees/clones at the same commit, so that creating
a new worktree and building is fast rather than rebuilding everything from
scratch.

## Current State

### What we shipped

One commit on the `runtime-incremental` branch:

- **`a99bee3a749`** — `DOTNET_RUNTIME_BUILD_CACHE` env var in `eng/build.sh`.
  When set to a directory path, the .NET SDK is installed to a shared location
  (`$DOTNET_RUNTIME_BUILD_CACHE/sdk`) via `DOTNET_GLOBAL_INSTALL_DIR`. This
  avoids redundant ~190MB SDK downloads across worktrees. Zero behavior change
  when unset.

### What we reverted

- **`097aa9684f9`** (reverted in `01eca8df95b`) — A shell-level task build
  cache that copied `artifacts/bin/{task}/` outputs and skipped `tasks.proj`
  via a `SkipTasksBuild` MSBuild property. Reverted because it's a layer
  violation: shell-level, won't work on Windows, won't work when invoking
  MSBuild directly.

## Build Execution Order for `./build.sh clr.aot`

`clr.aot` expands to `clr.alljits+clr.tools+clr.nativeaotlibs+clr.nativeaotruntime`.

| Phase | Time (cold) | Time (warm) | Description |
|-------|-------------|-------------|-------------|
| SDK install | ~60s + download | 0s (cached) | `eng/common/tools.sh` → `dotnet-install.sh` |
| NuGet restore | 1.7s | 1.5s | Static graph restore, packages in `~/.nuget/packages/` |
| `tasks.proj` | 2.0s | 0s (incremental) | HelixTestTasks, Crossgen2Tasks, installer.tasks |
| `runtime-prereqs.proj` | ~instant | ~instant | Generates `_version.c`, `runtime_version.h` |
| CMake configure | 25s | 1.3s | `build-runtime.sh` → `gen-buildsys.sh` → cmake |
| Ninja native build | 31s | 0.05s | JITs + NativeAOT runtime (C++) |
| Managed tools + libs | 52s | 2.3s | ILCompiler, crossgen2, NativeAOT libs |
| **Total** | **~112s** | **~6.5s** | |

"Cold" = fresh worktree with no `artifacts/`. "Warm" = incremental rebuild
with no source changes.

## Key Technical Findings

### How `eng/build.sh` orchestration works

1. `eng/build.sh` parses args, calls `eng/common/build.sh`
2. `eng/common/build.sh` → `Build()` → `InitializeToolset()` → `InitializeDotNetCli()`
   → SDK install (if needed)
3. `InitializeCustomToolset()` → sources `eng/restore-toolset.sh` (if it exists —
   currently doesn't). This runs **after** SDK install but **before** MSBuild.
4. MSBuild runs `Build.proj` (Arcade toolset) which evaluates `Subsets.props` and
   builds everything

### SDK install variables

| Variable | Purpose | Behavior |
|----------|---------|----------|
| `DOTNET_INSTALL_DIR` | "Use this existing SDK" | Read-only check: only used if it already has the right version. If missing, **ignored and overwritten**. |
| `DOTNET_GLOBAL_INSTALL_DIR` | "Install SDK here instead of repo-local" | Write target: SDK gets installed here if missing. Persists across worktrees. |

`DOTNET_INSTALL_DIR` is a standard dotnet variable. `DOTNET_GLOBAL_INSTALL_DIR`
is Arcade-specific. We use `DOTNET_GLOBAL_INSTALL_DIR` for the SDK cache.

### MSBuild's incremental build model

MSBuild uses **timestamp-based** Inputs/Outputs on targets:
- If all outputs are newer than all inputs → target skipped
- Breaks on fresh `git checkout` (all sources get "now" timestamp)
- Breaks when files are touched without content change

There is a `_GenerateCompileDependencyCache` target that hashes `@(Compile)`
items, but it hashes **item specs (file paths)**, not **file contents**. So it
detects add/remove of files but not content changes. Actual content change
detection still falls back to timestamps.

### MSBuild Project Cache Plugin (`ProjectCachePluginBase`)

MSBuild has a native plugin extension point for project-level caching:

- **Cache get** (`GetCacheResultAsync`): Before building each project, MSBuild
  asks the plugin if it can be skipped. Plugin receives the fully evaluated
  `ProjectInstance` with all resolved items (`@(Compile)`, `@(ReferencePath)`,
  `$(MSBuildAllProjects)`, etc.). Requires `/graph` flag.
- **Cache add** (`HandleProjectFinishedAsync`): After a cache miss build,
  MSBuild notifies the plugin with the `BuildResult`. File access info is
  only available with `/reportFileAccesses` (Windows-only, MSBuild.exe only).
- Plugin is injected via `<ProjectCachePlugin>` items in the project import
  graph (typically via a NuGet PackageReference).

### `microsoft/MSBuildCache` — the off-the-shelf implementation

- Uses Detours (Windows) for file access tracking to auto-discover inputs/outputs
- **Limitations**: Designed for clean CI builds only; requires
  `/graph /reportFileAccesses`; `/reportFileAccesses` is Windows-only and
  MSBuild.exe-only (not `dotnet`); doesn't work for incremental developer builds
- **Not usable for our scenario** on Linux

### `/inputResultsCache` and `/outputResultsCache`

These MSBuild flags serialize/deserialize `BuildResult` objects (target output
metadata). They do **NOT** compute cache keys or detect input changes. They are
the interface between a higher-order build engine (like BuildXL/CloudBuild) and
MSBuild:

- `/outputResultsCache:file` — save what happened
- `/inputResultsCache:file` — replay cached results for **dependencies** (not
  the top-level project itself)
- `/isolateProjects` — fail if a dependency isn't in the cache

These are designed for the "single project isolated build" scenario where an
external driver (BuildXL) handles all caching intelligence.

### How BuildXL/CloudBuild actually works

BuildXL acts as a driver above MSBuild:

1. Constructs the project graph (via MSBuild's static graph API)
2. For each project (bottom-up):
   - Computes a **two-level fingerprint**: weak (declared inputs) + strong
     (content hashes of actual input files)
   - Cache hit → materializes outputs, provides `BuildResult` via
     `/inputResultsCache`
   - Cache miss → builds in sandbox (Detours on Windows, LD_PRELOAD on Linux),
     observes file accesses, stores outputs + `BuildResult`
3. MSBuild is invoked per-project with `-isolateProjects`

Key insight: **MSBuild does no caching intelligence.** It's a dumb executor
with hooks. All cache key computation and hit/miss logic lives in BuildXL.

BuildXL works on Linux (LD_PRELOAD/ptrace for file access tracking), but
integrating it as a build orchestrator for dotnet/runtime would be a massive
undertaking.

## Recommended Next Step: Custom `ProjectCachePluginBase`

The most promising approach is a **custom MSBuild project cache plugin** that:

1. **Runs inside MSBuild** — no external driver, no shell scripts, works on
   all platforms, works with `./build.sh` and `dotnet build` alike
2. **Uses MSBuild's own evaluation for input discovery** — the plugin receives
   the `ProjectInstance` with all resolved `@(Compile)`, `@(ReferencePath)`,
   `$(MSBuildAllProjects)`, etc. No need for Detours.
3. **Content-hashes inputs** — hash the actual file contents of all evaluated
   inputs to produce a cache key (immune to timestamp issues)
4. **Caches selectively** — returns `CacheMiss` for projects it doesn't
   understand (like `runtime.proj` which shells out to CMake). Caches managed
   projects that are well-behaved.
5. **Stores outputs in `$DOTNET_RUNTIME_BUILD_CACHE`** — reuses the same
   cache root we already have for the SDK

### How it would work

The plugin would be a .NET assembly referenced via `<ProjectCachePlugin>` in
`Directory.Build.props` (conditionally, when `DOTNET_RUNTIME_BUILD_CACHE` is
set).

**`GetCacheResultAsync`** (called before each project build):
1. Receive the evaluated `ProjectInstance`
2. Extract `@(Compile)`, `@(ReferencePath)`, `$(MSBuildAllProjects)`, key
   properties
3. SHA-256 hash the **content** of each file + property values → cache key
4. Check `$DOTNET_RUNTIME_BUILD_CACHE/projects/{cache_key}/`
5. Hit → copy outputs to `artifacts/bin/`, return `CacheHit` with proxy
   targets or cached `BuildResult`
6. Miss → return `CacheMiss`

**`HandleProjectFinishedAsync`** (called after each cache-miss build):
1. Receive the `BuildResult`
2. Copy outputs from `artifacts/bin/{project}/` to cache
3. Serialize the `BuildResult` for future replay

### Open questions

- **`/graph` requirement**: Does `GetCacheResultAsync` work without `/graph`?
  The spec says `/graph` is required for cache-get. dotnet/runtime doesn't
  currently build with `/graph`. The VS workaround (static field discovery)
  might work but is described as temporary.
- **Performance of content hashing**: Hashing all `@(Compile)` +
  `@(ReferencePath)` + `$(MSBuildAllProjects)` file contents adds overhead.
  Need to measure whether this is cheaper than just building. For small
  projects like tasks, this should be fast. For large projects, might need
  an incremental hashing strategy.
- **Output completeness**: The plugin must materialize all outputs that
  downstream targets expect. Need to verify the full set of outputs for
  each project (bin/ files, obj/ marker files, etc.).
- **`BuildResult` replay correctness**: Returning a cached `BuildResult`
  must satisfy all downstream `MSBuild` task calls. Using proxy targets
  (e.g., `GetTargetPath`) may be safer than replaying full results.
- **Scope**: Start with just the 3 task projects (Crossgen2Tasks,
  HelixTestTasks, installer.tasks). They're simple, have no
  ProjectReferences, and rarely change.

### Prototype validation

A shell-script prototype demonstrated the core concept works:
- Content-based cache key: stable across `touch` (no false invalidation),
  changes when file content changes
- Cache hit: **44ms** (just `cp`) vs cache miss: **2.5s** (MSBuild build)
- The script hashed files with `sha256sum` and stored/restored
  `artifacts/bin/` directories

The plugin approach would achieve the same result but integrated into MSBuild
itself.
