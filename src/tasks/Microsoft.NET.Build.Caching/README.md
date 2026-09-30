# Disk cache prototype

This non-shipping project implements two internal storage interfaces without
BuildXL or a cache service. It does not implement task declarations or execution.

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
