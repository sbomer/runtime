# Declared-I/O task and disk cache prototype

This non-shipping, non-packable prototype provides `DeclaredIOTask` on top of two
internal storage interfaces, without BuildXL or a cache service. The task API is
public for experimentation, not an approved shipping API.

## Declaring a task

Derive from `DeclaredIOTask`, describe every dependency in `DescribeOperation`,
and implement `ExecuteCore(CancellationToken)`. The sealed `Execute()` method
performs lookup, execution on a miss, and publication.

```csharp
public sealed class CopyTask : DeclaredIOTask
{
    [Required]
    public string Source { get; set; } = "";

    [Required]
    public string Destination { get; set; } = "";

    [Output]
    public long Length { get; set; }

    protected override void DescribeOperation(TaskDeclaration declaration)
    {
        declaration.AddInputFile(Source);
        declaration.AddOutputFile(Destination);
        declaration.AddOutputValue(nameof(Length), () => Length, value => Length = value);
    }

    protected override bool ExecuteCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Copy(Source, Destination, overwrite: true);
        Length = new FileInfo(Destination).Length;
        return true;
    }
}
```

The example uses `System.IO`, `System.Threading`, `Microsoft.Build.Framework`,
and `Microsoft.NET.Build.Caching`. Task invocation must supply `CacheDirectory`,
even when caching is disabled. `CacheEnabled` defaults to false and is captured
before declaration. Importing `buildTransitive/Microsoft.NET.Build.Caching.props`
provides an overridable per-user `$(DeclaredIOCacheDirectory)`; pass that property
explicitly to the task. The props do not enable caching or create directories.

`AddValue<T>` records execution-affecting settings; `AddOutputValue<T>` and
`AddOutputItem` register output getters and setters. Supported values are strings,
Boolean, Char, fixed-width integers, Single, Double, Decimal, DateTime, ITaskItem,
and one-dimensional arrays of those types. Nullable value types, enums, and
jagged/multidimensional arrays are unsupported. Floating-point bits, decimal
scale, UTF-16 code units, and task-item escaping/custom metadata are preserved.
Enabled input values are snapshotted at registration. Disabled caching validates
names and types without serializing values or calling output getters.

The key includes loaded task/base module MVIDs, task type, normalized input paths
and content hashes, output paths, values and output schemas, working directory,
runtime, OS, architecture, cultures, and time zone. There is no reflection-based
parameter discovery. Task authors must declare helper/tool files and every other
dependency, keep inputs stable, and exclusively own outputs without filesystem
aliases. Hidden dependencies and side effects cannot be replayed.

Caching is intended for expensive deterministic operations, not as an optimization
for the copy example itself. A hit still hashes every input, verifies cached
content, and copies outputs. For cheap tasks, this can cost much more than running
the operation with caching disabled.

An index value identifies a manifest in CAS, containing output content hashes,
file metadata, and serialized output values. Output getters run before any puts;
warnings, errors, unsuccessful execution, and observed cancellation prevent
publication. Competing publishers must produce identical manifest hashes.
Corruption, missing referenced blobs, and conflicting results fail the task,
never silently fall back to execution. Diagnostics are not cached.

Hits restore bytes, read-only attributes/Unix modes, and fresh timestamps, then
invoke output setters. Replay unlinks existing outputs and copies into newly
created files, requesting mode 0600 on Unix before writing; final modes are
applied after the copy. Ownership and ACLs are not replayed. Produced symbolic
links and linked output ancestors are rejected. Replay is not transactional:
failure or cancellation can leave partial outputs, and setters may have side
effects. As with failed execution, callers must not consume failed task outputs.

## Storage API

`DiskCache(directory)` is a lightweight owner exposing `Cas` and `Index`.
Construction and index misses do not create storage. Independent objects,
threads, and processes can use the same directory concurrently.

| Interface | Operation | Result |
| --- | --- | --- |
| `IContentAddressableStore` | `PutAsync(Stream, CancellationToken)` | SHA-256 hash of stored bytes |
| `IContentAddressableStore` | `GetAsync(ContentHash, CancellationToken)` | Verified, read-only stream |
| `ICacheIndex` | `GetAsync(CacheKey, CancellationToken)` | Stored hash, or null if absent |
| `ICacheIndex` | `GetOrAddAsync(CacheKey, ContentHash, CancellationToken)` | Winning hash, whether new or existing |

`ContentHash` and `CacheKey` are distinct immutable 32-byte SHA-256 values.
Their constructors copy the digest; their byte-array conversions return copies.
The default value represents an all-zero digest, not an absent value.

## Contracts

CAS puts read from the current source position to EOF, copying and hashing with
a bounded pooled buffer. They support non-seekable streams and leave the source
open. Existing blobs are verified before reuse and are never overwritten or
repaired. Empty content is stored like any other blob.

CAS reads hash the opened file before returning the same handle, rewound to zero.
Verification costs one complete read before consumption. The caller owns and
disposes the stream; it is independent of the `DiskCache` object's lifetime.
Missing blobs and corrupt content are errors.

The index is independent of the CAS: it neither requires the mapped content to
exist nor interprets manifests. Callers must put all required content before
publishing a mapping and compare the returned winning hash to detect conflicting
results. Existing mappings never change. Only an absent index record returns
null; malformed records and storage failures throw.

Operations use asynchronous stream I/O, not `Task.Run`. Directory creation,
opening/closing files, hashing, and publication include synchronous work.
Cancellation prevents publication when observed before the atomic publish; it does
not roll back a publication that already completed. Source failures and
cancellation clean up unpublished temporary files during ordinary unwinding.

## Filesystem model

The versioned layout is:

```text
DiskCache.v1/
  cas/<first two hash characters>/<remaining hash characters>.blob
  index/<first two key characters>/<remaining key characters>.entry
  tmp/<unique identifier>.tmp
```

An index record contains the key, value, and SHA-256 checksum of those two fields
(96 bytes total). The checksum detects accidental corruption, not malicious
modification. All records and blobs are written to private temporary files and
closed before atomic create-if-absent publication. Unix uses `link` followed by
removal of the temporary name; Windows uses `MoveFileExW` without replacement
or cross-volume copying. No caller-owned file is hardlinked. `File.Move` is not
used because its Unix implementation can check for absence and then overwrite
a concurrent winner with `rename`. Competing publishers read the winner. No
database, in-memory authoritative index, or directory-owner lock is involved.

Use a trusted local filesystem supporting atomic, non-overwriting publication
within the cache root. Network shares and malicious concurrent filesystem
changes are outside the contract. Symlink/reparse-point paths are rejected.
On modern .NET, newly created Unix directories request mode 0700 and files 0600,
subject to the process umask; existing directory permissions are preserved.
Windows uses inherited ACLs. The .NET Framework target supports Windows only.

There is no eviction, quota, expiration, or automatic cleanup. Clients must not
delete or mutate published entries while any client is active. Consequently this
version needs no sessions or maintenance leases. Future concurrent cleanup would
require a coordination protocol and a way to discover manifest references; it
cannot simply delete apparently unused blobs. Crashes can leave temporary files
or unreferenced content. Publication gives atomic visibility, not power-loss
durability.

## Building

```sh
./build.sh tasks --projects "$PWD/src/tasks/Microsoft.NET.Build.Caching/Microsoft.NET.Build.Caching.csproj"
./build.sh tasks --projects "$PWD/src/tasks/Microsoft.NET.Build.Caching.Tests/Microsoft.NET.Build.Caching.Tests.csproj" --test
```

The product targets the repository's current .NET and .NET Framework tooling
frameworks. Tests execute on current .NET. Compiling the .NET Framework target
does not establish Windows execution coverage.
