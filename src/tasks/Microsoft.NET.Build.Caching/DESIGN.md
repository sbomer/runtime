# Task-level caching

## Purpose

`Microsoft.NET.Build.Caching` prototypes a `DeclaredIOTask` base class derived
from `Microsoft.Build.Utilities.Task`. It represents a deterministic managed-task
operation with explicitly declared inputs and outputs. Caching is an independently
configurable optimization, not the meaning of deriving from the base class.
The contract applies whether caching is enabled or disabled: the same declared
inputs and execution settings must produce the same declared result, without
undeclared dependencies or side effects. Declarations are the task author's
promise, not sandbox-enforced proof of determinism.

This is an implemented prototype, not an approved public API. It requires
neither a project-cache plugin nor a graph build. The base does not launch tools;
tool invocation is one possible derived implementation. A separate `ToolTask`
adapter is outside this design.

## Explicit cache contract

Derived tasks declare every input file, expected output file, and value that
affects execution. A single fingerprint covers input paths and content hashes,
declared output paths, task implementation/cache-format identity, and declared
execution settings. Toolchain identity, arguments, response-file contents,
working directory, environment values, and platform distinctions are included
when they affect the operation. Toolchain identity must account for supporting
files that affect results, not just the executable's name or version string.
Collections with set semantics are sorted; meaningful ordering is preserved.

Existing output contents and timestamps are not fingerprint inputs: a clean build
must still find cached results. Paths remain location-sensitive initially;
cross-checkout reuse is out of scope. Inputs must remain stable during execution.
Tasks with undeclared dependencies, in-place input modification, variable
undeclared outputs, or side effects beyond the declared result do not satisfy
the contract. There is no file-access monitoring, dependency discovery, or
two-phase fingerprinting.

## Execution and replay

The base owns lookup, execution, result capture, and replay. Derived tasks supply
`DescribeOperation(TaskDeclaration)` and synchronous `ExecuteCore(CancellationToken)`.
Declarations register supported typed values and output getter/setter delegates;
the base owns serialization without inspecting task properties. Both cached and uncached execution use the same
declarations and operation. Disabling caching bypasses cache lookup and
publication, not the deterministic execution contract.

With caching enabled, the base computes the fingerprint and looks up a versioned
result manifest. A hit restores all declared output files and serialized MSBuild
output values, including item metadata, before returning success. `ITaskItem2`
item specs and metadata preserve their escaped representation; plain `ITaskItem`
values are escaped as literals. Reconstruction does not normalize item-spec
backslashes as path separators. Restoration
preserves required file attributes, such as executable permissions, and makes
output timestamps suitable for subsequent timestamp-based incremental checks.
The captured metadata is the read-only flag and Unix permission bits. Ownership
and ACLs are not replayed. Produced symlinks are rejected to avoid a regular-file
cache hit differing from live execution.

`CacheDirectory` is unconditionally required. Package props supply the overridable
per-user `DeclaredIOCacheDirectory` property, which each task invocation binds.
`CacheEnabled` defaults to false and is independent of the fingerprint.

Task identity includes the full derived type name and the loaded module version
IDs (MVIDs) of the task and base library, not their current on-disk file contents.
Post-build rewriting that changes task behavior must regenerate the affected MVIDs.
Helper assemblies still require explicit declarations and are content-hashed,
as are other declared files. The fingerprint also includes working directory, runtime,
OS/architecture, culture names, and local time-zone rules. Floating-point values
are encoded as exact bits rather than culture-dependent strings.

On a miss, the operation executes normally. Only successful, uncancelled,
warning-free executions with all declared outputs present are eligible for
publication. Outputs are captured into immutable storage before the task returns,
so later build steps cannot change the cached result. An entry becomes visible
only after its blobs and manifest are complete. Existing outputs do not
themselves count as a hit; derived tasks must ensure successful execution
produces the declared result rather than accepting stale files.

## Storage and failures

Storage uses BuildXL's `ConcurrentLocalCache` through its combined `ICache` and
`ICacheSession` content/memoization interfaces. The package owns the filesystem
implementation; there are no parallel runtime store contracts or native helpers.
It uses immutable blobs and atomically created fingerprint-to-content-list entries,
safe for cooperating concurrent processes, without a database or shutdown-time
index snapshot. Use SHA-256 hashes and `CacheDeterminism.Tool`. Publish all
referenced blobs before the memoization entry. Equivalent
publishers succeed without replacing the winner; conflicting results fail.
Entries preserve ordered hashes and exact optional payload bytes, matching
BuildXL content hash list equality. Callers own canonical ordering, manifest
serialization, and explicitly listing the manifest and all referenced blobs.
Restored files must not permit modification of cached blobs. The cache enables
owner-only file placement: Linux destinations start with mode 0600 before any
bytes are copied. The task applies recorded Unix permissions only after placement
and verification succeed, so partial or corrupt copies remain owner-only.

Short-lived sessions hold a shared cross-process maintenance lock through lookup
and restoration or through publication, but not through task execution. Session
shutdown cancels and drains outstanding operations; operations within one session
are serialized. Returned streams own independent
shared leases and can outlive their sessions. Future maintenance must acquire the
exclusive side of the same lock. Eviction and size configuration
are not implemented; growth is unbounded. Remote storage, asynchronous publication,
and diagnostic replay are out of scope initially. Atomic visibility is required,
but losing recent entries on a machine crash is acceptable.

BuildXL operations return result objects that callers must check. Only a
successful lookup with a null content hash list is a cache miss. Missing referenced blobs,
corruption, and publication conflicts are hard errors, with no automatic execution
fallback. Lookup and publication check content availability without hashing;
verified copy placement hashes the destination after copying. Stream opening
does not verify hashes, so the task layer must verify manifest bytes before
deserializing them. The prototype verifies the manifest hash and validates its
paths, content references, and typed output values before placement.
Readers do not delete stale entries. Restoration copies directly to
destinations rather than staging the entire result; a failure may leave partial
or corrupt outputs. Cancellation is honored rather than treated as a miss.
Manifest paths are validated against declared destinations before writing.
Binlog messages distinguish hits, misses, bypasses, and cache failures without
exposing declared setting values.

The package version is an unpublished prototype placeholder. The backend supports
Windows and Linux local filesystems and uses its own cache format; it does not
reuse or migrate the earlier runtime implementation's data. See the README for
lifecycle, platform restrictions, and local package-feed wiring.

## Prior art

The design borrows MSBuildCache's separation of
[fingerprinting](https://github.com/microsoft/MSBuildCache/blob/54bcfb23a927bead6622eee0cd3f109ae66c9931/src/Common/Fingerprinting/IFingerprintFactory.cs),
[content storage](https://github.com/microsoft/MSBuildCache/blob/54bcfb23a927bead6622eee0cd3f109ae66c9931/src/Common/Caching/CacheClient.cs),
and [result replay](https://github.com/microsoft/MSBuildCache/blob/54bcfb23a927bead6622eee0cd3f109ae66c9931/src/Common/NodeBuildResult.cs),
plus its requirement to capture output content before downstream work can
overwrite it. It deliberately does not adopt project-level integration or
weak/strong fingerprint lookup.
