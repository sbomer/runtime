// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.NET.Build.Caching;

/// <summary>Provides immutable disk storage shared by independent, cooperating processes.</summary>
/// <remarks>
/// Construction performs no I/O. No daemon or exclusive lifetime owner is required.
/// Files must not be modified or removed while clients use the cache. There is no eviction.
/// </remarks>
internal sealed class DiskCache : IContentAddressableStore, ICacheIndex
{
    private const int BufferSize = 65536;
    private const int IndexDataLength = Sha256Digest.Length * 2;
    private const int IndexRecordLength = IndexDataLength + Sha256Digest.Length;
    private readonly string _root;

    internal DiskCache(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(SR.CacheDirectoryRequired, nameof(directory));
        }

#if NETFRAMEWORK
        if (Path.DirectorySeparatorChar != '\\')
        {
            throw new PlatformNotSupportedException(SR.UnsupportedFrameworkPlatform);
        }
#endif
        _root = Path.Combine(Path.GetFullPath(directory), "DiskCache.v1");
    }

    internal IContentAddressableStore Cas => this;
    internal ICacheIndex Index => this;

    async ValueTask<ContentHash> IContentAddressableStore.PutAsync(Stream source, CancellationToken cancellationToken)
    {
        if (!source.CanRead)
        {
            throw new ArgumentException(SR.UnreadableSource, nameof(source));
        }

        cancellationToken.ThrowIfCancellationRequested();
        string temporary = CreateTemporaryPath();
        bool ownsTemporary = false;
        try
        {
            ContentHash hash;
            using (FileStream destination = CreateFile(temporary))
            {
                ownsTemporary = true;
                hash = await CopyAndHashAsync(source, destination, cancellationToken).ConfigureAwait(false);
            }

            string path = GetPath("cas", hash.ToString(), ".blob");
            CreateDirectory(Path.GetDirectoryName(path)!);
            if (!Publish(temporary, path, cancellationToken))
            {
                using Stream existing = await Cas.GetAsync(hash, cancellationToken).ConfigureAwait(false);
            }

            return hash;
        }
        finally
        {
            if (ownsTemporary)
            {
                File.Delete(temporary);
            }
        }
    }

    async ValueTask<Stream> IContentAddressableStore.GetAsync(ContentHash hash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = GetPath("cas", hash.ToString(), ".blob");
        RejectLinks(path);
        var stream = OpenFile(path);
        try
        {
            ContentHash actual = await CopyAndHashAsync(stream, destination: null, cancellationToken).ConfigureAwait(false);
            if (actual != hash)
            {
                throw new InvalidDataException(SR.Format(SR.CorruptContent, path));
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    async ValueTask<ContentHash?> ICacheIndex.GetAsync(CacheKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = GetPath("index", key.ToString(), ".entry");
        RejectLinks(path);
        FileStream stream;
        try
        {
            stream = OpenFile(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        using (stream)
        {
            // One extra byte detects oversized records without trusting their length.
            var bytes = new byte[IndexRecordLength + 1];
            int length = 0;
            while (length < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes, length, bytes.Length - length, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (length != IndexRecordLength || new CacheKey(bytes.AsSpan(0, Sha256Digest.Length)) != key)
            {
                throw new InvalidDataException(SR.Format(SR.CorruptIndex, path));
            }

            using SHA256 sha256 = SHA256.Create();
            byte[] checksum = sha256.ComputeHash(bytes, 0, IndexDataLength);
            if (!checksum.AsSpan().SequenceEqual(bytes.AsSpan(IndexDataLength, Sha256Digest.Length)))
            {
                throw new InvalidDataException(SR.Format(SR.CorruptIndex, path));
            }

            return new ContentHash(bytes.AsSpan(Sha256Digest.Length, Sha256Digest.Length));
        }
    }

    async ValueTask<ContentHash> ICacheIndex.GetOrAddAsync(CacheKey key, ContentHash value, CancellationToken cancellationToken)
    {
        ContentHash? existing = await Index.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (existing.HasValue)
        {
            return existing.Value;
        }

        var record = new byte[IndexRecordLength];
        key.ToByteArray().CopyTo(record, 0);
        value.ToByteArray().CopyTo(record, Sha256Digest.Length);
        using (SHA256 sha256 = SHA256.Create())
        {
            sha256.ComputeHash(record, 0, IndexDataLength).CopyTo(record, IndexDataLength);
        }

        cancellationToken.ThrowIfCancellationRequested();
        string temporary = CreateTemporaryPath();
        bool ownsTemporary = false;
        try
        {
            using (FileStream stream = CreateFile(temporary))
            {
                ownsTemporary = true;
                await stream.WriteAsync(record, 0, record.Length, cancellationToken).ConfigureAwait(false);
            }

            string path = GetPath("index", key.ToString(), ".entry");
            CreateDirectory(Path.GetDirectoryName(path)!);
            if (Publish(temporary, path, cancellationToken))
            {
                return value;
            }

            return await Index.GetAsync(key, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException(SR.Format(SR.MissingWinningEntry, path));
        }
        finally
        {
            if (ownsTemporary)
            {
                File.Delete(temporary);
            }
        }
    }

    private string GetPath(string store, string hash, string extension) =>
        Path.Combine(_root, store, hash.Substring(0, 2), hash.Substring(2) + extension);

    private string CreateTemporaryPath()
    {
        string directory = Path.Combine(_root, "tmp");
        CreateDirectory(directory);
        return Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
    }

    private static void CreateDirectory(string path)
    {
        RejectLinks(path);
        string? parent = Path.GetDirectoryName(path);
        if (parent is not null && !Directory.Exists(parent))
        {
            // The Unix-mode overload only applies the requested mode to the leaf.
            CreateDirectory(parent);
        }

#if NETFRAMEWORK
        Directory.CreateDirectory(path);
#else
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
#endif
        RejectLinks(path);
    }

    private static FileStream CreateFile(string path)
    {
#if NETFRAMEWORK
        return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
#else
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            UnixCreateMode = OperatingSystem.IsWindows() ? null : UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
#endif
    }

    private static FileStream OpenFile(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static bool Publish(string temporary, string path, CancellationToken cancellationToken)
    {
        RejectLinks(path);
        cancellationToken.ThrowIfCancellationRequested();
        return DiskCacheFileSystem.Publish(temporary, path);
    }

    private static async ValueTask<ContentHash> CopyAndHashAsync(Stream source, Stream? destination, CancellationToken cancellationToken)
    {
        using SHA256 hash = SHA256.Create();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (read == 0)
                {
                    break;
                }

                hash.TransformBlock(buffer, 0, read, buffer, 0);
                if (destination is not null)
                {
                    await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                }
            }

            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return new ContentHash(hash.Hash!);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static void RejectLinks(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(SR.Format(SR.LinkedCachePath, current));
            }

            if (current != path && (attributes & FileAttributes.Directory) == 0)
            {
                throw new IOException(SR.Format(SR.NonDirectoryCachePath, current));
            }
        }
    }
}
