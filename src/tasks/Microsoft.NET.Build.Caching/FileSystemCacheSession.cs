// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.NET.Build.Caching;

internal sealed class FileSystemCacheSession : ICacheSession
{
    private const int BufferSize = 81920;
    private readonly FileSystemCacheStore _store;
    private readonly FileStream _lease;
    private readonly object _lifetimeLock = new object();
    private readonly TaskCompletionSource<bool> _disposed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _closing;
    private int _operations;

    internal FileSystemCacheSession(FileSystemCacheStore store, FileStream lease)
    {
        _store = store;
        _lease = lease;
    }

    public async Task<ContentHash> PutFileAsync(string sourcePath, CancellationToken cancellationToken)
    {
        using IDisposable operation = BeginOperation();
        cancellationToken.ThrowIfCancellationRequested();
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        return await PutStreamCoreAsync(source, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ContentHash> PutStreamAsync(Stream source, CancellationToken cancellationToken)
    {
        using IDisposable operation = BeginOperation();
        return await PutStreamCoreAsync(source, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ContentHash> PutStreamCoreAsync(Stream source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string temporaryPath = _store.TemporaryPath();
        try
        {
            ContentHash hash;
            using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                hash = await CopyAndHashAsync(source, destination, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!CacheFileSystem.Publish(temporaryPath, _store.BlobPath(hash)))
            {
                using FileStream existing = await OpenVerifiedAsync(hash, cancellationToken).ConfigureAwait(false);
            }

            return hash;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public async Task<Stream> OpenStreamAsync(ContentHash hash, CancellationToken cancellationToken)
    {
        using IDisposable operation = BeginOperation();
        FileStream lease = await CacheFileSystem.AcquireLockAsync(_store.LockPath, exclusive: false, cancellationToken).ConfigureAwait(false);
        bool transferred = false;
        try
        {
            FileStream stream = await OpenVerifiedAsync(hash, cancellationToken).ConfigureAwait(false);
            var result = new LeasedReadStream(stream, lease);
            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            {
                lease.Dispose();
            }
        }
    }

    public async Task PlaceFileAsync(ContentHash hash, string destinationPath, CancellationToken cancellationToken)
    {
        using IDisposable operation = BeginOperation();
        string fullPath = Path.GetFullPath(destinationPath);
        string root = _store.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(root, CacheFileSystem.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new IOException(SR.Format(SR.CacheDestinationInsideStore, fullPath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using FileStream source = OpenContent(hash);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        // Detach an existing hard link or symlink rather than modifying its target.
        // A competing writer that creates the destination afterward is an error.
        File.Delete(fullPath);
        using var destination = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        ContentHash actual = await CopyAndHashAsync(source, destination, cancellationToken).ConfigureAwait(false);
        if (!actual.Equals(hash))
        {
            throw new InvalidDataException(SR.Format(SR.CacheContentCorrupt, hash));
        }
    }

    public Task<CacheEntry?> GetAsync(Fingerprint key, CancellationToken cancellationToken)
    {
        using IDisposable operation = BeginOperation();
        cancellationToken.ThrowIfCancellationRequested();
        CacheEntry? entry = ReadEntry(key);
        if (entry is not null)
        {
            CheckContent(entry, cancellationToken);
        }

        return Task.FromResult(entry);
    }

    public Task<PublishResult> AddOrGetAsync(Fingerprint key, CacheEntry entry, CancellationToken cancellationToken)
    {
        using IDisposable operation = BeginOperation();
        cancellationToken.ThrowIfCancellationRequested();
        CacheEntry? existing = ReadEntry(key);
        if (existing is not null)
        {
            return Task.FromResult(Compare(key, existing, entry, cancellationToken));
        }

        CheckContent(entry, cancellationToken);
        string temporaryPath = _store.TemporaryPath();
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                entry.Write(stream);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (CacheFileSystem.Publish(temporaryPath, _store.EntryPath(key)))
            {
                return Task.FromResult(PublishResult.Added);
            }

            existing = ReadEntry(key) ?? throw new InvalidDataException(SR.Format(SR.CacheEntryDisappeared, key));
            return Task.FromResult(Compare(key, existing, entry, cancellationToken));
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private PublishResult Compare(Fingerprint key, CacheEntry existing, CacheEntry candidate, CancellationToken cancellationToken)
    {
        CheckContent(existing, cancellationToken);
        if (!existing.Equals(candidate))
        {
            throw new InvalidDataException(SR.Format(SR.CacheConflict, key));
        }

        return PublishResult.Equivalent;
    }

    private CacheEntry? ReadEntry(Fingerprint key)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(_store.EntryPath(key), FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        using (stream)
        {
            return CacheEntry.Read(stream);
        }
    }

    private void CheckContent(CacheEntry entry, CancellationToken cancellationToken)
    {
        foreach (ContentHash hash in entry.ContentHashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using FileStream stream = OpenContent(hash);
            _ = stream.Length;
        }
    }

    private FileStream OpenContent(ContentHash hash) =>
        new FileStream(_store.BlobPath(hash), FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);

    private async Task<FileStream> OpenVerifiedAsync(ContentHash hash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileStream stream = OpenContent(hash);
        bool verified = false;
        try
        {
            ContentHash actual = await CopyAndHashAsync(stream, Stream.Null, cancellationToken).ConfigureAwait(false);
            if (!actual.Equals(hash))
            {
                throw new InvalidDataException(SR.Format(SR.CacheContentCorrupt, hash));
            }

            stream.Position = 0;
            verified = true;
            return stream;
        }
        finally
        {
            if (!verified)
            {
                stream.Dispose();
            }
        }
    }

    private static async Task<ContentHash> CopyAndHashAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] buffer = new byte[BufferSize];
        int count;
#if NETFRAMEWORK
        while ((count = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0)
#else
        while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
#endif
        {
            sha256.TransformBlock(buffer, 0, count, buffer, 0);
#if NETFRAMEWORK
            await destination.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
#else
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
#endif
        }

        cancellationToken.ThrowIfCancellationRequested();
        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return ContentHash.FromBytes(sha256.Hash!);
    }

    private Operation BeginOperation()
    {
        lock (_lifetimeLock)
        {
#if NETFRAMEWORK
            if (_closing)
            {
                throw new ObjectDisposedException(nameof(FileSystemCacheSession));
            }
#else
            ObjectDisposedException.ThrowIf(_closing, this);
#endif

            _operations++;
            return new Operation(this);
        }
    }

    private void EndOperation()
    {
        lock (_lifetimeLock)
        {
            _operations--;
            CompleteDisposal();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifetimeLock)
        {
            _closing = true;
            CompleteDisposal();
            return new ValueTask(_disposed.Task);
        }
    }

    private void CompleteDisposal()
    {
        if (_closing && _operations == 0 && !_disposed.Task.IsCompleted)
        {
            _lease.Dispose();
            _disposed.SetResult(true);
        }
    }

    private sealed class Operation : IDisposable
    {
        private FileSystemCacheSession? _session;
        internal Operation(FileSystemCacheSession session) => _session = session;
        public void Dispose() => Interlocked.Exchange(ref _session, null)?.EndOperation();
    }
}
