# ILLink task cache: v1 decisions

These are design decisions. Eligible task invocations compute a content-based key, restore a cached output directory on a hit, or run ILLink and store successful results.

## Scope and configuration

- Cache complete ILLink invocations in the MSBuild task, not in the linker command-line implementation.
- Keep v1 simple. Reuse results at the same input/output paths; defer cross-worktree reuse and retain absolute paths in cache identity.
- Add an `EnableCache` task parameter, defaulting to `false`, supplied by `EnableILLinkCache`. Disabled caching performs no cache I/O, even if a cache directory already exists.
- Add an optional `CacheDirectory` task parameter, supplied by `ILLinkCacheDirectory`. Selecting a directory does not enable caching.
- Use platform-specific default directories: `$XDG_CACHE_HOME/illink` on Linux/other XDG Unix, falling back to `$HOME/.cache/illink`; `$HOME/Library/Caches/illink` on macOS; `LocalApplicationData/illink` on Windows.
- Resolve defaults inside `ILLinkCache.TryCreate` through a private helper, not in targets or the task parameter getter. The parameter remains the explicit override; resolve relative overrides against the current working directory.
- Ignore relative `XDG_CACHE_HOME` values. If no usable user location is available, skip caching with a diagnostic rather than fall back to the working directory or shared temporary storage.
- Do not add a dedicated ILLink environment-variable configuration mechanism. Use normal MSBuild property configuration and precedence.
- Do not promise stability or migration support for the on-disk format.

## API shape

```csharp
internal sealed class ILLinkCache
{
    internal static ILLinkCache? TryCreate(string? cacheDirectory, TaskLoggingHelper log);
    internal bool TryRestore(string inputHash, string outputDirectory, DateTime outputTimestampUtc);
    internal void Store(string inputHash, string outputDirectory);
}
```

- The task owns input identity and execution. Its private `TryComputeCacheKey` combines command-line and response-file arguments with input-content and toolchain identity; inability to compute a key bypasses caching with a diagnostic.
- `TryCreate` resolves the cache directory and returns null with a diagnostic if initialization cannot proceed.
- `TryRestore` combines lookup and restoration. True means complete restoration. Missing entries and expected I/O, access, or invalid-cache-data failures return false with appropriate logging and fall back to normal linking. Copies overwrite existing files and are not rolled back if a later copy fails. Unexpected errors and cancellation propagate.
- `Store` is best-effort. Lock contention and existing entries are normal skips; expected cache I/O failures are logged without failing a successful link. Unexpected errors and cancellation propagate.
- Expected restore/store failures are `IOException`, `UnauthorizedAccessException`, `InvalidDataException`, and `FormatException`, not every exception. Invalid helper arguments remain programming errors.
- After normal task validation, resolve the cache directory before computing the key so an unusable configuration does not trigger input inspection or hashing. If resolution succeeds, compute the key once and attempt restoration. On a hit, skip linking; otherwise link normally and store only with exit code zero and no logged errors. A failed restore never substitutes for a successful fallback link.

## Execution and publication

- Check the cache before normal tool execution. On a hit, validate the complete entry, then delete/recreate the destination and copy the cached tree. On any miss or restore failure, the normal execution path deletes/recreates the directory before running ILLink; no separate rollback or failure-cleanup path is needed.
- The task owns its output directory, including when caching is disabled. Normal-execution cleanup failures fail the task. Rely on the supplied `OutputDirectory` parameter without additional task path/input validation; callers must provide a dedicated directory. The command-line linker retains its existing nonempty-directory behavior.
- ILLink writes to its normal output directory. After successful linking, copy results into the cache; do not redirect linker output to a temporary directory.
- Let cache misses compute independently. Scheduling and execution times are nondeterministic, and steps can fail or be aborted; one computation must not block another's progress.
- Use a unique cache staging directory for each store attempt, including attempts with the same input hash.
- Acquire a per-input-hash writer lock, scoped to the cache directory, after linking and before staging copies.
- Acquire the lock nonblocking. If busy, skip storing rather than wait; this avoids duplicate cache-copy work, not duplicate linking. The losing task keeps its own computed outputs and does not wait or retry if the winning writer later fails or is aborted.
- Under the lock, recheck whether the entry already exists. If absent, populate staging and publish with an atomic, same-filesystem directory rename without overwriting an existing entry.
- Rely on the documented `Directory.Move` contract that an existing destination causes `IOException`, even if that destination is empty. The writer lock avoids duplicate staging work; it is not a workaround for publication semantics. Do not add platform-specific interop or require non-empty entries to compensate for runtime/filesystem bugs. Atomic visibility does not imply power-loss durability.
- Create cache directories lazily in `Store`, after acquiring the writer lock and confirming the entry is absent. `TryCreate` only resolves the path; `TryRestore` treats a missing directory as a miss. Directory-creation failures are logged without failing a successful link.
- Keep published entries immutable and read them without locks.
- Use a named `System.Threading.Mutex` derived from the resolved cache-directory path and input hash. An abandoned mutex grants ownership; recheck the entry and use fresh staging. This coordinates processes on one machine, not writers on different machines sharing a network directory.
- Support concurrent task invocations with distinct output directories sharing a cache directory. Concurrent linking to the same output directory is unsupported.
- Treat missing or unreadable cache data as a failed restore, never a successful hit.
- Start without replaying warnings or other linker diagnostics on cache hits. This is an intentional v1 limitation.

## Input identity and eligibility

- Use SHA-256 over a versioned, length-delimited description. Preserve argument ordering and absolute paths, including the working and output directories.
- Hash the contents of all supplied assemblies, reference assemblies, and root descriptors. Include adjacent PDBs, MDBs, configuration files, and satellite assemblies, even when symbol emission is disabled. Follow assembly metadata file entries for linked resources and additional modules. Optional-file additions and removals change the key; timestamps alone do not.
- Use the linker assembly's `AssemblyInformationalVersion` as the identity of its whole distribution, including bundled dependencies and configuration. Read it from assembly metadata without loading the linker; bypass caching with a diagnostic if the version is missing, empty, or unreadable. Do not hash the linker binary or scan its directory. This assumes an immutable, versioned distribution: local changes to ILLink, Cecil, or deployment configuration must also change ILLink's informational version, or use a cleared/isolated cache or disable caching. A new informational version invalidates prior entries even for local builds.
- Continue hashing the task assembly and selected dotnet host executable. Do not enumerate or hash the host's installed hostfxr/shared-runtime trees: doing so makes lookup cost scale with every installed framework/version, including ones unrelated to the link. Runtime/tool files supplied as linker inputs remain content-hashed.
- Defer identifying the runtime that executes ILLink; do not include a runtime version or framework description in the key. The task runs in MSBuild, so its CoreLib informational version and framework description are not reliable identities for the child linker's runtime. The runtimeconfig describes requested frameworks and roll-forward policy, not the exact resolved CoreLib. Finding that CoreLib would require resolved-runtime information from the caller, host-resolution plumbing, or an additional process; defer that complexity and startup cost.
- This deliberately accepts that runtime installation changes not represented by other keyed inputs may reuse an entry; callers needing invalidation for those changes must clear or isolate the cache.
- Include platform, architecture, and culture. Do not enumerate or hash inherited environment variables, or bypass caching based on them. This deliberately keeps v1 simple rather than maintaining a partial list of runtime/loader settings. Environment changes are reflected only when they change other keyed inputs, such as the selected host path or culture. Callers must disable caching when inherited settings introduce otherwise untracked dependencies, affect outputs, or require tool-execution side effects (for example, startup hooks or profilers).
- Bypass caching for nonempty `ExtraArgs`, custom steps/data, dependency-dump options, and explicit `ToolTask.EnvironmentVariables` overrides. These are caller-supplied inputs that v1 does not model in its key and can introduce undeclared dependencies, external outputs, or side effects. SDK options passed through `ExtraArgs` are subject to the same restriction.
- Log and bypass caching if required files cannot be read or an input assembly cannot be inspected, including PE images without managed metadata. Normal linking determines whether those inputs are acceptable. Input files must remain stable during hashing and linking.

## Entry layout

- Store entries under `<cache>/v1/<input-hash>/`, with a binary `manifest` and an `outputs/` tree.
- The manifest records the format version, output paths, lengths, and SHA-256 content hashes. Validate it and every listed file before starting restoration.
- Copy the complete output tree without special-casing filenames. The MSBuild target owns `<output-directory>.semaphore` (normally `linked.semaphore`) and invalidates it before the incremental skip decision when that directory is missing. There is no special migration or exclusion for old in-directory `Link.semaphore` files; ordinary whole-directory cleanup and caching apply to them. Empty directories are not recorded.
- The restore helper replaces the destination tree after entry validation. Copy failures can leave partial results, which the task's normal execution cleanup removes before fallback linking. Publication is atomic; restoration is not.
- Assume ILLink outputs are not symbolic links; do not check for them. Cache and output directories must not contain each other.
- Stamp restored files with the UTC timestamp captured before input-key inspection. This avoids stale cache timestamps in downstream incremental checks without making outputs appear newer than inputs changed during inspection or restoration.

## Cache lifetime

- No eviction policy or deletion of published entries; users may manually wipe the cache directory.
- Do not repair or replace corrupt published entries. Restoration falls back to linking, but storage still skips an existing entry until the user removes it.
- No `created` or `last-used` bookkeeping markers.
- Attempt best-effort cleanup of the current attempt's unpublished staging directory. Log cleanup I/O/access failures without failing a successful link; do not scan or delete other attempts' staging directories. Correctness must tolerate leftovers after crashes.

## Open follow-ups

- Revisit identifying the resolved runtime/CoreLib that executes ILLink without adding substantial cache-lookup overhead.
- Revisit symbolic-link handling when generalizing beyond ILLink outputs.
- Consider explicit/manual or CI purging; the no-entry-deletion scope remains unchanged until decided otherwise.
- Reconsider diagnostic replay, including missing console/binlog warnings and effects on external warning-as-error policies.
- Examine manual wiping racing reads, including safe fallback after partially restoring output files.
