# Microsoft.NET.Build.Caching

This project provides internal filesystem caching infrastructure for a future
library of reusable MSBuild task base classes. It does not yet contain task
execution, output-property replay, or public task-authoring APIs.

## Filesystem backend

`FileSystemCacheStore` implements separate content and memoization interfaces
through short-lived sessions, without BuildXL dependencies. Multiple processes
can independently open the same directory. Construction does not access storage;
opening the first session initializes a versioned format marker.
The ordered-list entry format uses version 2; version 1 cache directories are
rejected without modification and require a separate cache directory.

Content consists of immutable SHA-256 blobs. Publish all referenced blobs,
including a canonical result manifest, before calling `AddOrGetAsync`. The manifest
is opaque to the backend; its caller owns output paths, attributes, and serialized
MSBuild property values. `CacheEntry` preserves an ordered list of hashes,
including duplicates, and an optional opaque payload of at most 1 KiB. Equality
matches BuildXL's content hash list semantics: hash order and exact payload bytes
matter, and null differs from an empty payload. Callers own canonical ordering
and must explicitly include the manifest and every blob it references. The
backend does not discover references inside the manifest or payload.

Publication creates a complete entry only if absent. An equivalent publication
succeeds without replacing the winner; a conflicting publication fails. An absent
memoization entry is a miss. Lookup and publication check that all listed blobs
can be opened, without reading their contents. Missing content is an error.
Placement verifies hashes while copying; opening a content stream verifies its
hash before returning any bytes, including manifest bytes. Corruption detected
during retrieval is an error, never an execution fallback. Reads do not remove
or invalidate entries. Puts hash while copying and verify an existing blob when
losing publication to it.

A session holds a shared cross-process maintenance lock. Independent sessions and
their reads and writes remain concurrent. Session disposal stops accepting new
operations and waits for outstanding operations before releasing its lock.
Returned streams own independent shared leases and remain usable after session
disposal; dispose them to release their leases. Finish pending reads before
disposing a stream. Keep task execution outside sessions; use one session for
lookup and restoration, and another for publication after execution succeeds.

The lock helper supports exclusive acquisition for future maintenance, but there
is no eviction, size setting, or quota enforcement. Growth is unbounded. No
database or shutdown-time index snapshot is used. Publication provides atomic
visibility, not power-loss durability. Process crashes can leave temporary files
or unreferenced blobs; automatic cleanup is not implemented.

Restoration copies directly to caller-owned destinations, without staging the
entire result, and may leave partial or corrupt output on failure. Hash mismatch
is reported after copying, not before touching the destination. Existing
destination files are unlinked before copying, avoiding mutation of an existing
hard-link or symlink target. The caller must ensure exclusive ownership of output paths and
that paths (including parent directories and aliases) do not resolve into the
cache. The store is not a security boundary against malicious filesystem changes.

Use a private cache directory shared only by cooperating clients. Never delete
or replace `maintenance.lock` while the store can be in use. Filesystems must
support file locking and atomic publication: Unix uses `flock` and hard-link
publication; Windows uses file-sharing exclusion and non-replacing `MoveFileW`.
Unsupported operations fail rather than falling back to unsafe copying.
Exclusive lock acquisition is cancellable, but writer fairness is not guaranteed.
Network filesystems and non-cooperating cleanup tools are outside the validated
configuration.

## Build and test

From the repository root:

```sh
./build.sh tasks --projects "$PWD/src/tasks/Microsoft.NET.Build.Caching/Microsoft.NET.Build.Caching.csproj" --pack
./build.sh tasks --projects "$PWD/src/tasks/Microsoft.NET.Build.Caching.Tests/Microsoft.NET.Build.Caching.Tests.csproj" --test
```

Tests cover synchronized multi-process startup, readers observing concurrent
publication, equivalent/conflicting and late publishers, malformed records,
missing/corrupt content, copy isolation, partial restoration, cancellation,
cancellation-resistant I/O draining, independent stream leases, competing
exclusive waiters, and process-death lock release. These common scenarios are
also covered by BuildXL's `ConcurrentCacheTestTool`; BuildXL-only selectors,
bulk APIs, realization modes, and unverified stream reads retain separate tests.
The library targets modern .NET and .NET Framework; tests run on the repository's
current tooling .NET. Windows and macOS execution require those platforms.

## Packaging

The project targets the repository's current .NET and .NET Framework tool target
frameworks. It inherits MSBuild assembly references from `src/tasks/Directory.Build.targets`;
those assemblies are supplied by the MSBuild host, not bundled in this package.

The NuGet package uses the standard `lib/<target-framework>` layout so task
projects can reference the library at compile time and distribute it alongside
their own task assemblies. Task authors will need their own MSBuild references.
There are no automatic `UsingTask` registrations because the package is intended
to provide base classes rather than concrete tasks.

The project is automatically included by `src/tasks/tasks.proj`. It is packable
but marked non-shipping until its API and distribution contract are established.
