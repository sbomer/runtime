# Microsoft.NET.Build.Caching

This project provides internal filesystem caching infrastructure for a future
library of reusable MSBuild task base classes. It does not yet contain task
execution, output-property replay, or public task-authoring APIs.

## Filesystem backend

`CacheStore.Create` constructs BuildXL's `ConcurrentLocalCache`, exposed through
its `ICache` and `ICacheSession` interfaces. Storage, locking, content hashes,
memoization records, and result types come from the
`Microsoft.BuildXL.Cache.MemoizationStore.Library` package, not a parallel runtime
implementation. Multiple processes can independently open the same directory
without a cache service or lifetime-exclusive store owner.

Construction does not access storage. Call and check `StartupAsync` on the cache,
then `CreateSession` and `StartupAsync` on each session before use. Check operation
results: an unsuccessful BuildXL result is an error, not an exception or a cache
miss. Shut down and dispose sessions and the cache when finished. Only a
successful `GetContentHashListAsync` result with a null content hash list is a miss.

Use SHA-256 content hashes and publish every referenced blob, including a
canonical result manifest, before `AddOrGetContentHashListAsync`. Supply
`CacheDeterminism.Tool`. `ContentHashList` preserves hash order and duplicates,
plus an optional opaque payload of at most 1 KiB. Exact payload bytes matter;
null and empty differ. Equivalent publication succeeds without replacing the
winner; conflicting publication returns `InvalidToolDeterminismError` with the
winning value. Callers own canonical ordering, manifest serialization, and
explicitly listing every referenced blob. The backend does not inspect manifests.

Sessions hold shared cross-process maintenance leases. Separate sessions and
processes remain concurrent, but operations **within one session are serialized**.
Shutdown cancels queued/running session work and drains it before releasing the
lease. Returned streams have independent leases and can outlive their sessions
and cache; the caller must dispose them. Keep task execution outside sessions:
use one for lookup/restoration and another for publication after execution.

Lookup and publication check content availability, not hashes. Missing referenced
content is an error. **`OpenStreamAsync` does not verify content integrity**;
callers must verify a manifest's hash before trusting its contents. For output
restoration, use verified `FileRealizationMode.Copy`, not `CopyNoVerify`. Placement
copies and then verifies the destination; failure can leave partial or corrupt
outputs. Do not treat corruption or conflicts as execution fallbacks. Readers do
not delete or repair entries. Puts copy and hash their temporary files and verify
an existing blob when losing publication to it.

There is no eviction, quota, size setting, database, or shutdown-time index
snapshot. Growth is unbounded. Future maintenance must take the exclusive side
of the backend's lock. Atomic visibility, not power-loss durability, is provided;
crashes can leave temporary files or unreferenced blobs.

The backend supports Windows and Linux on local filesystems with the required
locking and atomic-publication primitives. It rejects reparse-point/symlink
ancestors and destinations inside its store. Use a private directory shared only
by cooperating clients, and exclusively own restoration destinations. Never
delete or replace the store lock while it may be in use. Network filesystems,
non-cooperating cleanup tools, and malicious concurrent filesystem changes are
outside this contract. macOS is not supported by this backend.

BuildXL uses its own `ConcurrentLocalCache.v1` subdirectory and format. It does
not read, migrate, or delete the earlier runtime backend's version 1 or 2 data.
This also replaces the old backend's verified stream opening, parallel
same-session operations, and runtime-specific error contracts.

## Unpublished dependency and local development

The version in `eng/Versions.props` is an **unpublished prototype placeholder**
for BuildXL's `ConcurrentLocalCache` implementation. A clean restore is expected
to fail until that package is available. The public BuildXL feed is configured,
but existing published packages without this type are not substitutes.

To use locally built packages, supply an additional feed without editing tracked
project files:

```sh
./build.sh tasks --projects "$PWD/src/tasks/Microsoft.NET.Build.Caching/Microsoft.NET.Build.Caching.csproj" --pack -p:RestoreAdditionalProjectSources="$BUILDXL_PACKAGE_FEED"
./build.sh tasks --projects "$PWD/src/tasks/Microsoft.NET.Build.Caching.Tests/Microsoft.NET.Build.Caching.Tests.csproj" --test -p:RestoreAdditionalProjectSources="$BUILDXL_PACKAGE_FEED"
```

`BUILDXL_PACKAGE_FEED` must contain that exact package version, with the new backend
and compatible compile/runtime assets for both tooling target frameworks. If an
actual BuildXL build produces a different version, also pass
`-p:MicrosoftBuildXLVersion=<version>`. Keep local feeds, package-generation
projects, and source-path overrides outside tracked files. A locally assembled
validation package is not an official BuildXL release and must not be published.

Integration tests exercise the package boundary: lazy construction, lifecycle,
persistent content/memoization round trips, ordered/payload equality and conflicts,
missing/corrupt content errors, independent streams, and concurrent process
publication. Detailed storage/locking/format tests belong to BuildXL's
`ConcurrentCacheTestTool`. Tests run on the repository's current tooling .NET;
.NET Framework builds do not constitute Windows execution coverage.

## Packaging

The project targets the repository's current .NET and .NET Framework tool target
frameworks. It inherits MSBuild assembly references from
`src/tasks/Directory.Build.targets`; the MSBuild host supplies those assemblies.

The NuGet package uses the standard `lib/<target-framework>` layout and declares
BuildXL as a package dependency rather than bundling its sources. Consumers must
deploy the resolved dependency closure alongside their task assemblies; this is
substantially larger than the former dependency-free backend. Direct references
raise two transitive dependencies above their known vulnerable versions. Task
authors also need their own MSBuild references. There are no automatic `UsingTask`
registrations because this package will provide base classes rather than tasks.

The project is automatically included by `src/tasks/tasks.proj`. It is packable
but non-shipping until its API, dependency version, and distribution contract are
established.
