// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.NET.Build.Caching;

internal sealed class FileSystemCacheStore : ICacheStore
{
    private static readonly byte[] s_format = Encoding.ASCII.GetBytes("DeclaredIOCache\n2\nSHA256\n");

    internal FileSystemCacheStore(string directory) => DirectoryPath = Path.GetFullPath(directory);

    internal string DirectoryPath { get; }
    internal string LockPath => Path.Combine(DirectoryPath, "maintenance.lock");
    internal string BlobPath(ContentHash hash) => Path.Combine(DirectoryPath, "content", hash.Hex);
    internal string EntryPath(Fingerprint key) => Path.Combine(DirectoryPath, "entries", key.Hex);
    internal string TemporaryPath() => Path.Combine(DirectoryPath, "tmp", Guid.NewGuid().ToString("N"));

    public async ValueTask<ICacheSession> OpenSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(DirectoryPath);
        FileStream lease = await CacheFileSystem.AcquireLockAsync(LockPath, exclusive: false, cancellationToken).ConfigureAwait(false);
        bool initialized = false;
        try
        {
            Initialize();
            cancellationToken.ThrowIfCancellationRequested();
            var session = new FileSystemCacheSession(this, lease);
            initialized = true;
            return session;
        }
        finally
        {
            if (!initialized)
            {
                lease.Dispose();
            }
        }
    }

    private void Initialize()
    {
        Directory.CreateDirectory(Path.Combine(DirectoryPath, "tmp"));
        string formatPath = Path.Combine(DirectoryPath, "format");
        try
        {
            ValidateFormat(formatPath);
        }
        catch (FileNotFoundException)
        {
            string temporaryPath = TemporaryPath();
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(s_format, 0, s_format.Length);
                }

                CacheFileSystem.Publish(temporaryPath, formatPath);
                ValidateFormat(formatPath);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }

        Directory.CreateDirectory(Path.Combine(DirectoryPath, "content"));
        Directory.CreateDirectory(Path.Combine(DirectoryPath, "entries"));
    }

    private static void ValidateFormat(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        if (stream.Length != s_format.Length || !reader.ReadBytes(s_format.Length).SequenceEqual(s_format))
        {
            throw new InvalidDataException(SR.Format(SR.InvalidCacheFormat, path));
        }
    }
}
