# Microsoft.NET.Build.Caching

This project prototypes `DeclaredIOTask`, a reusable deterministic MSBuild task
base with optional caching. Its public surface is experimental and has not been
approved for submission or shipping.

## Declaring a task

```csharp
public sealed class TransformFile : DeclaredIOTask
{
    [Required]
    public string Source { get; set; } = "";

    [Required]
    public string Destination { get; set; } = "";

    public float Factor { get; set; }

    [Output]
    public ITaskItem? Result { get; private set; }

    protected override void DescribeOperation(TaskDeclaration declaration)
    {
        declaration.AddInputFile(Source);
        declaration.AddOutputFile(Destination);
        declaration.AddValue(nameof(Factor), Factor);
        declaration.AddOutputItem(nameof(Result), () => Result, value => Result = value);
    }

    protected override bool ExecuteCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.WriteAllText(Destination, File.ReadAllText(Source) +
            Factor.ToString("R", CultureInfo.InvariantCulture));
        Result = new TaskItem(Destination);
        return true;
    }
}
```

After registering the derived task with `UsingTask`, bind the inherited required
parameter on **every** invocation:

```xml
<TransformFile Source="input.txt" Destination="output.txt" Factor="1.25"
               CacheDirectory="$(DeclaredIOCacheDirectory)" CacheEnabled="true">
  <Output TaskParameter="Result" ItemName="TransformedFiles" />
</TransformFile>
```

The package's `build`/`buildTransitive` props supply an overridable per-user
`DeclaredIOCacheDirectory`. There is no C# directory fallback or private
environment variable. `CacheEnabled` defaults to false; disabled execution still
validates the declarations and output files, but never opens or creates the cache.

`AddValue<T>` snapshots an input value at declaration time when caching is enabled.
With caching disabled, it validates the name and supported type without traversing
or serializing the value. Each execution captures `CacheEnabled` before calling
`DescribeOperation`; changing the property during execution does not change that
execution's cache mode.
`AddOutputValue<T>` registers a getter and setter; the getter captures successful
execution and the setter replays a hit. There is no property reflection. Supported
types are `string`, `bool`, `char`, the eight fixed-width integer types, `float`,
`double`, `decimal`, `DateTime`, `ITaskItem`, and their one-dimensional arrays.
Null reference values, null arrays, and null string/item array elements are
preserved. Enums, nullable value types, arbitrary objects, and jagged or
multidimensional arrays are rejected. Floating-point bits and decimal scale are
preserved; item specs, custom metadata, defining-project information, and escaped
characters round-trip. `ITaskItem2` item specs and metadata retain their escaped
representation, including the distinction between lists and literal semicolons;
plain `ITaskItem` values are escaped as literals. Every output property that needs
replay must be registered.

Fingerprints include normalized input paths and SHA-256 content hashes, output
destinations, named input values, output value schemas, the full task type name,
and the loaded module version IDs (MVIDs) of the task and base library. They also
include the working directory, runtime, OS/architecture, culture names, and serialized local time-zone
rules (which affect `DateTime` binary round-tripping).
MVIDs identify the loaded code without rereading assembly files, so replacing a
DLL on disk cannot misidentify code already loaded by MSBuild. Post-build rewriting
that changes task behavior must regenerate the affected MVIDs. Task authors must
declare helper assemblies, toolchains, environment values, and any other
execution-affecting dependencies. Declared files, including explicitly declared
helper assemblies and tools, still use SHA-256 content hashes. Assembly dependency
discovery is not performed.

On a miss, execution runs synchronously on the calling thread, outside cache
sessions. Successful execution must produce every declared output. Errors,
cancellation, and warnings observed through the task's build engine prevent
publication, including diagnostics from output getters. Warning/error tracking
forwards the host's supported `IBuildEngine` interfaces. Diagnostics are not
replayed. Cancellation is cooperative; it is passed to execution and cache
operations, while startup/shutdown follow the backend's lifetime APIs.

Cached outputs preserve bytes, the read-only flag, and Unix permission bits.
On Linux, replay creates files with owner-only read/write permissions (0600)
before copying any bytes, then applies the recorded permissions after successful
verification. Failed or cancelled placement does not broaden those permissions.
Restoration gives files fresh modification times; ownership, ACLs, and other
filesystem-specific metadata are not replayed. Symlink outputs and linked output
ancestors are rejected, including during uncached execution, so a hit cannot
silently change a produced link into a regular file. Inputs must remain stable,
outputs must be exclusively owned, and input/output aliases must not overlap.
These are author/caller contracts, not filesystem sandboxing.

## Filesystem backend

`CacheStore.Create` constructs BuildXL's `ConcurrentLocalCache` with
`useOwnerOnlyFilePlacement: true`, exposed through
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
publication, plus actual MSBuild float binding and output replay, typed value
serialization, cancellation, diagnostics, corruption, and file metadata.
Detailed storage/locking/format tests belong to BuildXL's
`ConcurrentCacheTestTool`. Tests run on the repository's current tooling .NET;
.NET Framework builds do not constitute Windows execution coverage. The task's
.NET Framework target is Windows-only; use the modern .NET target on Linux.

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
registrations because this package provides a base class rather than concrete tasks.

The project is automatically included by `src/tasks/tasks.proj`. It is packable
but non-shipping until its API, dependency version, and distribution contract are
established.
