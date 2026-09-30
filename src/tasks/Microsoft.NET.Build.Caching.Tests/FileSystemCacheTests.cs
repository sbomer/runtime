// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.RemoteExecutor;
using Xunit;

namespace Microsoft.NET.Build.Caching.Tests;

public sealed class FileSystemCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "declared-io-cache-tests", Guid.NewGuid().ToString("N"));
    private FileSystemCacheStore Store => new FileSystemCacheStore(Path.Combine(_root, "cache"));
    private static Fingerprint Key(string text) => new Fingerprint(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));

    [Theory]
    [InlineData("")]
    [InlineData("cached output")]
    [InlineData("non-ascii \u00e9 \ud83d\ude00")]
    public async Task RoundTripAndReopen(string text)
    {
        FileSystemCacheStore store = Store;
        Assert.False(Directory.Exists(store.DirectoryPath));
        CacheEntry entry;
        ContentHash hash;
        await using (ICacheSession session = await store.OpenSessionAsync(CancellationToken.None))
        {
            Assert.Null(await session.GetAsync(Key("key"), CancellationToken.None));
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(text));
            hash = await session.PutStreamAsync(source, CancellationToken.None);
            Assert.True(source.CanRead);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), hash.Hex);
            entry = new CacheEntry(new[] { hash, hash }, new byte[] { 1, 2 });
            Assert.Equal(2, entry.ContentHashes.Count);
            Assert.Equal(PublishResult.Added, await session.AddOrGetAsync(Key("key"), entry, CancellationToken.None));
            Assert.Equal(PublishResult.Equivalent, await session.AddOrGetAsync(Key("key"), entry, CancellationToken.None));
        }

        await using ICacheSession reopened = await new FileSystemCacheStore(store.DirectoryPath).OpenSessionAsync(CancellationToken.None);
        Assert.Equal(entry, await reopened.GetAsync(Key("key"), CancellationToken.None));
        using (Stream stream = await reopened.OpenStreamAsync(hash, CancellationToken.None))
        using (var reader = new StreamReader(stream))
        {
            Assert.False(stream.CanWrite);
            Assert.Equal(text, await reader.ReadToEndAsync());
        }

        string output = Path.Combine(_root, "output");
        await reopened.PlaceFileAsync(hash, output, CancellationToken.None);
        Assert.Equal(text, File.ReadAllText(output));
        File.WriteAllText(output, "modified");
        Assert.Equal(text, File.ReadAllText(store.BlobPath(hash)));
        ContentHash changed = await reopened.PutFileAsync(output, CancellationToken.None);
        Assert.NotEqual(hash, changed);
        Assert.Empty(Directory.GetFiles(Path.Combine(store.DirectoryPath, "tmp")));
    }

    [Fact]
    public async Task ConflictsPreserveWinner()
    {
        await using ICacheSession session = await Store.OpenSessionAsync(CancellationToken.None);
        ContentHash first = await PutAsync(session, "first");
        ContentHash second = await PutAsync(session, "second");
        var winner = new CacheEntry(new[] { first });
        var loser = new CacheEntry(new[] { second });
        Assert.Equal(PublishResult.Added, await session.AddOrGetAsync(Key("key"), winner, CancellationToken.None));
        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => session.AddOrGetAsync(Key("key"), loser, CancellationToken.None));
        Assert.Contains(Key("key").Hex, error.Message);
        Assert.Equal(winner, await session.GetAsync(Key("key"), CancellationToken.None));
    }

    [Theory]
    [InlineData("equivalent")]
    [InlineData("reordered")]
    [InlineData("duplicate")]
    [InlineData("payload-bytes")]
    [InlineData("payload-length")]
    [InlineData("null-empty")]
    [InlineData("empty-null")]
    public async Task MemoizationUsesOrderedHashesAndExactPayload(string difference)
    {
        FileSystemCacheStore store = Store;
        await using ICacheSession session = await store.OpenSessionAsync(CancellationToken.None);
        ContentHash first = await PutAsync(session, "first");
        ContentHash second = await PutAsync(session, "second");
        byte[]? payload = difference switch
        {
            "null-empty" => null,
            "empty-null" => Array.Empty<byte>(),
            _ => new byte[] { 1, 2 }
        };
        var winner = new CacheEntry(new[] { first, second }, payload);
        CacheEntry candidate = difference switch
        {
            "reordered" => new CacheEntry(new[] { second, first }, payload),
            "duplicate" => new CacheEntry(new[] { first, second, second }, payload),
            "payload-bytes" => new CacheEntry(new[] { first, second }, new byte[] { 2, 1 }),
            "payload-length" => new CacheEntry(new[] { first, second }, new byte[] { 1 }),
            "null-empty" => new CacheEntry(new[] { first, second }, Array.Empty<byte>()),
            "empty-null" => new CacheEntry(new[] { first, second }),
            _ => new CacheEntry(new[] { first, second }, payload)
        };
        await session.AddOrGetAsync(Key("key"), winner, CancellationToken.None);
        byte[] originalEntry = File.ReadAllBytes(store.EntryPath(Key("key")));
        if (difference == "equivalent")
        {
            Assert.Equal(winner, candidate);
            Assert.Equal(winner.GetHashCode(), candidate.GetHashCode());
            Assert.Equal(PublishResult.Equivalent, await session.AddOrGetAsync(Key("key"), candidate, CancellationToken.None));
        }
        else
        {
            Assert.NotEqual(winner, candidate);
            await Assert.ThrowsAsync<InvalidDataException>(() => session.AddOrGetAsync(Key("key"), candidate, CancellationToken.None));
        }

        Assert.Equal(winner, await session.GetAsync(Key("key"), CancellationToken.None));
        Assert.Equal(originalEntry, File.ReadAllBytes(store.EntryPath(Key("key"))));
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(0, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 1024)]
    public async Task EntriesPreserveCallerOrderAndSnapshotArrays(int hashCount, int payloadLength)
    {
        await using ICacheSession session = await Store.OpenSessionAsync(CancellationToken.None);
        ContentHash first = await PutAsync(session, "first");
        ContentHash second = await PutAsync(session, "second");
        ContentHash[] hashes = hashCount == 0 ? Array.Empty<ContentHash>() : new[] { second, first, second };
        byte[]? payload = payloadLength == -1 ? null : Enumerable.Repeat((byte)42, payloadLength).ToArray();
        var expected = new CacheEntry(hashes, payload);
        var entry = new CacheEntry(hashes, payload);
        if (hashes.Length != 0)
        {
            hashes[0] = first;
        }

        if (payloadLength > 0)
        {
            payload![0] = 0;
        }

        Assert.Equal(expected, entry);
        Assert.Equal(PublishResult.Added, await session.AddOrGetAsync(Key("key"), entry, CancellationToken.None));
        CacheEntry? restored = await session.GetAsync(Key("key"), CancellationToken.None);
        Assert.Equal(expected, restored);
        Assert.Equal(hashCount, restored!.ContentHashes.Count);
        Assert.Equal(payloadLength, restored.Payload?.Count ?? -1);
    }

    [Fact]
    public void OversizedPayloadIsRejected()
    {
        Assert.Throws<ArgumentException>("payload", () => new CacheEntry(Array.Empty<ContentHash>(), new byte[1025]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingContentFailsLookupAndCorruptionFailsRetrieval(bool corrupt)
    {
        FileSystemCacheStore store = Store;
        await using ICacheSession session = await store.OpenSessionAsync(CancellationToken.None);
        ContentHash hash = await PutAsync(session, "content");
        var entry = new CacheEntry(new[] { hash });
        await session.AddOrGetAsync(Key("key"), entry, CancellationToken.None);
        byte[] originalEntry = File.ReadAllBytes(store.EntryPath(Key("key")));
        if (corrupt)
        {
            File.WriteAllText(store.BlobPath(hash), "corrupt");
        }
        else
        {
            File.Delete(store.BlobPath(hash));
        }

        Type error = corrupt ? typeof(InvalidDataException) : typeof(FileNotFoundException);
        if (corrupt)
        {
            Assert.Equal(entry, await session.GetAsync(Key("key"), CancellationToken.None));
            Assert.Equal(PublishResult.Equivalent, await session.AddOrGetAsync(Key("key"), entry, CancellationToken.None));
            using var original = new MemoryStream(Encoding.UTF8.GetBytes("content"));
            await Assert.ThrowsAsync<InvalidDataException>(() => session.PutStreamAsync(original, CancellationToken.None));
            Assert.Equal("corrupt", File.ReadAllText(store.BlobPath(hash)));
        }
        else
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => session.GetAsync(Key("key"), CancellationToken.None));
            await Assert.ThrowsAsync<FileNotFoundException>(() => session.AddOrGetAsync(Key("key"), entry, CancellationToken.None));
        }

        await Assert.ThrowsAsync(error, () => session.OpenStreamAsync(hash, CancellationToken.None));
        string destination = Path.Combine(_root, "untouched");
        File.WriteAllText(destination, "original");
        await Assert.ThrowsAsync(error, () => session.PlaceFileAsync(hash, destination, CancellationToken.None));
        Assert.Equal(corrupt ? "corrupt" : "original", File.ReadAllText(destination));
        Assert.Equal(originalEntry, File.ReadAllBytes(store.EntryPath(Key("key"))));
    }

    [Fact]
    public async Task PublicationRequiresAllListedContent()
    {
        await using ICacheSession session = await Store.OpenSessionAsync(CancellationToken.None);
        ContentHash hash = await PutAsync(session, "content");
        var entry = new CacheEntry(new[] { hash, new ContentHash(new string('0', 64)) });
        await Assert.ThrowsAsync<FileNotFoundException>(() => session.AddOrGetAsync(Key("key"), entry, CancellationToken.None));
        Assert.Null(await session.GetAsync(Key("key"), CancellationToken.None));
    }

    [Theory]
    [InlineData("equivalent")]
    [InlineData("conflict")]
    [InlineData("missing")]
    public async Task PublicationAfterMissObservesLateWinner(string outcome)
    {
        FileSystemCacheStore store = Store;
        await using ICacheSession observer = await store.OpenSessionAsync(CancellationToken.None);
        Assert.Null(await observer.GetAsync(Key("key"), CancellationToken.None));
        await using ICacheSession publisher = await store.OpenSessionAsync(CancellationToken.None);
        ContentHash winningHash = await PutAsync(publisher, "winner");
        ContentHash ownHash = outcome == "equivalent" ? winningHash : await PutAsync(observer, "own output");
        var winner = new CacheEntry(new[] { winningHash });
        var candidate = new CacheEntry(new[] { ownHash });
        await publisher.AddOrGetAsync(Key("key"), winner, CancellationToken.None);
        byte[] record = File.ReadAllBytes(store.EntryPath(Key("key")));
        string output = Path.Combine(_root, "own-output");
        File.WriteAllText(output, "own output");
        if (outcome == "missing")
        {
            File.Delete(store.BlobPath(winningHash));
            await Assert.ThrowsAsync<FileNotFoundException>(() => observer.AddOrGetAsync(Key("key"), candidate, CancellationToken.None));
        }
        else if (outcome == "conflict")
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => observer.AddOrGetAsync(Key("key"), candidate, CancellationToken.None));
        }
        else
        {
            Assert.Equal(PublishResult.Equivalent, await observer.AddOrGetAsync(Key("key"), candidate, CancellationToken.None));
        }

        Assert.Equal(record, File.ReadAllBytes(store.EntryPath(Key("key"))));
        Assert.Equal("own output", File.ReadAllText(output));
    }

    [Fact]
    public async Task PutFileSnapshotsInputAndPartialRestorationDoesNotRollBack()
    {
        await using ICacheSession session = await Store.OpenSessionAsync(CancellationToken.None);
        Directory.CreateDirectory(_root);
        string input = Path.Combine(_root, "input");
        File.WriteAllText(input, "original");
        ContentHash hash = await session.PutFileAsync(input, CancellationToken.None);
        File.WriteAllText(input, "changed input");
        string output = Path.Combine(_root, "first-output");
        await session.PlaceFileAsync(hash, output, CancellationToken.None);
        string missingOutput = Path.Combine(_root, "second-output");
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            session.PlaceFileAsync(new ContentHash(new string('0', 64)), missingOutput, CancellationToken.None));
        Assert.Equal("original", File.ReadAllText(output));
        Assert.False(File.Exists(missingOutput));
    }

    [Fact]
    public async Task PreCanceledOperationsDoNotChangeStorageOrOutputs()
    {
        FileSystemCacheStore store = Store;
        await using ICacheSession session = await store.OpenSessionAsync(CancellationToken.None);
        ContentHash hash = await PutAsync(session, "content");
        var entry = new CacheEntry(new[] { hash });
        await session.AddOrGetAsync(Key("key"), entry, CancellationToken.None);
        byte[] record = File.ReadAllBytes(store.EntryPath(Key("key")));
        string output = Path.Combine(_root, "output");
        File.WriteAllText(output, "original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.GetAsync(Key("key"), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.AddOrGetAsync(Key("new"), entry, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.OpenStreamAsync(hash, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.PlaceFileAsync(hash, output, cancellation.Token));
        using var source = new MemoryStream(new byte[] { 1 });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.PutStreamAsync(source, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.PutFileAsync(output, cancellation.Token));
        Assert.Equal(0, source.Position);
        Assert.Equal("original", File.ReadAllText(output));
        Assert.Equal(record, File.ReadAllBytes(store.EntryPath(Key("key"))));
        Assert.Null(await session.GetAsync(Key("new"), CancellationToken.None));
        Assert.Single(Directory.GetFiles(Path.Combine(store.DirectoryPath, "content")));
        Assert.Empty(Directory.GetFiles(Path.Combine(store.DirectoryPath, "tmp")));
    }

    [Fact]
    public async Task ReadOnlyStreamsAndInvalidDestinationsCannotModifyContent()
    {
        FileSystemCacheStore store = Store;
        await using ICacheSession session = await store.OpenSessionAsync(CancellationToken.None);
        ContentHash hash = await PutAsync(session, "content");
        using (Stream stream = await session.OpenStreamAsync(hash, CancellationToken.None))
        {
            Assert.Throws<NotSupportedException>(() => stream.WriteByte(1));
            Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        }

        await Assert.ThrowsAsync<IOException>(() => session.PlaceFileAsync(hash, store.BlobPath(hash), CancellationToken.None));
        string directory = Path.Combine(_root, "directory");
        Directory.CreateDirectory(directory);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.PlaceFileAsync(hash, directory, CancellationToken.None));
        Assert.Equal("content", File.ReadAllText(store.BlobPath(hash)));
    }

    [Fact]
    public async Task ContentIsInvisibleUntilPublicationCompletes()
    {
        FileSystemCacheStore store = Store;
        await using ICacheSession writer = await store.OpenSessionAsync(CancellationToken.None);
        await using ICacheSession reader = await store.OpenSessionAsync(CancellationToken.None);
        using var source = new PausedReadStream();
        Task<ContentHash> put = writer.PutStreamAsync(source, CancellationToken.None);
        try
        {
            await source.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(Directory.GetFiles(Path.Combine(store.DirectoryPath, "content")));
            Assert.Null(await reader.GetAsync(Key("key"), CancellationToken.None));
        }
        finally
        {
            source.Resume.TrySetResult(true);
        }

        ContentHash hash = await put;
        var entry = new CacheEntry(new[] { hash });
        await writer.AddOrGetAsync(Key("key"), entry, CancellationToken.None);
        Assert.Equal(entry, await reader.GetAsync(Key("key"), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(40)]
    public async Task MalformedEntriesAreNotMisses(int length)
    {
        FileSystemCacheStore store = Store;
        await using ICacheSession session = await store.OpenSessionAsync(CancellationToken.None);
        File.WriteAllBytes(store.EntryPath(Key("key")), new byte[length]);
        await Assert.ThrowsAsync<InvalidDataException>(() => session.GetAsync(Key("key"), CancellationToken.None));
    }

    [Theory]
    [InlineData(1, 0, -1, 0)]
    [InlineData(2, -1, -1, 0)]
    [InlineData(2, int.MaxValue, -1, 0)]
    [InlineData(2, 0, -2, 0)]
    [InlineData(2, 0, 1025, 1025)]
    [InlineData(2, 0, 1, 0)]
    [InlineData(2, 0, 0, 1)]
    [InlineData(2, 0, -1, 1)]
    public async Task InvalidEntryLengthsAndVersionsAreRejected(int version, int hashCount, int payloadLength, int payloadBytes)
    {
        FileSystemCacheStore store = Store;
        await using ICacheSession session = await store.OpenSessionAsync(CancellationToken.None);
        using (var stream = File.Create(store.EntryPath(Key("key"))))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(version);
            writer.Write(hashCount);
            writer.Write(payloadLength);
            writer.Write(new byte[payloadBytes]);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => session.GetAsync(Key("key"), CancellationToken.None));
    }

    [Fact]
    public async Task TruncatedRecordsFailWithoutReplacement()
    {
        FileSystemCacheStore store = Store;
        await using ICacheSession session = await store.OpenSessionAsync(CancellationToken.None);
        ContentHash hash = await PutAsync(session, "content");
        var entry = new CacheEntry(new[] { hash }, new byte[] { 1, 2 });
        await session.AddOrGetAsync(Key("key"), entry, CancellationToken.None);
        string path = store.EntryPath(Key("key"));
        byte[] valid = File.ReadAllBytes(path);
        for (int length = 0; length < valid.Length; length++)
        {
            byte[] truncated = valid.Take(length).ToArray();
            File.WriteAllBytes(path, truncated);
            await Assert.ThrowsAsync<InvalidDataException>(() => session.GetAsync(Key("key"), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => session.AddOrGetAsync(Key("key"), entry, CancellationToken.None));
            Assert.Equal(truncated, File.ReadAllBytes(path));
        }
    }

    [Theory]
    [InlineData("future-version")]
    [InlineData("DeclaredIOCache\n1\nSHA256\n")]
    public async Task UnsupportedFormatIsRejectedWithoutReplacement(string contents)
    {
        FileSystemCacheStore store = Store;
        Directory.CreateDirectory(store.DirectoryPath);
        string format = Path.Combine(store.DirectoryPath, "format");
        File.WriteAllText(format, contents);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.OpenSessionAsync(CancellationToken.None));
        Assert.Equal(contents, File.ReadAllText(format));
    }

    [Fact]
    public async Task StreamsOutliveSessionsAndExcludeMaintenance()
    {
        FileSystemCacheStore store = Store;
        ICacheSession first = await store.OpenSessionAsync(CancellationToken.None);
        ICacheSession second = await store.OpenSessionAsync(CancellationToken.None);
        ContentHash hash = await PutAsync(first, "content");
        using Stream stream = await first.OpenStreamAsync(hash, CancellationToken.None);
        using Stream otherStream = await first.OpenStreamAsync(hash, CancellationToken.None);
        await first.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => first.GetAsync(Key("key"), CancellationToken.None));
        using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CacheFileSystem.AcquireLockAsync(store.LockPath, exclusive: true, timeout.Token));
        }

        await second.DisposeAsync();
        stream.Dispose();
        using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CacheFileSystem.AcquireLockAsync(store.LockPath, exclusive: true, timeout.Token));
        }

        using (var reader = new StreamReader(otherStream, leaveOpen: true))
        {
            Assert.Equal("content", await reader.ReadToEndAsync());
        }

        otherStream.Dispose();
        using FileStream maintenance = await CacheFileSystem.AcquireLockAsync(store.LockPath, exclusive: true, CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.OpenSessionAsync(cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOpenStreamReleasesIndependentLease(bool corrupt)
    {
        FileSystemCacheStore store = Store;
        await using (ICacheSession session = await store.OpenSessionAsync(CancellationToken.None))
        {
            ContentHash hash = await PutAsync(session, "content");
            if (corrupt)
            {
                File.WriteAllText(store.BlobPath(hash), "corrupt");
            }
            else
            {
                File.Delete(store.BlobPath(hash));
            }

            await Assert.ThrowsAsync(corrupt ? typeof(InvalidDataException) : typeof(FileNotFoundException),
                () => session.OpenStreamAsync(hash, CancellationToken.None));
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using FileStream maintenance = await CacheFileSystem.AcquireLockAsync(store.LockPath, exclusive: true, timeout.Token);
    }

    [Fact]
    public async Task CanceledWriteDoesNotPublishAndDisposalWaitsForOperation()
    {
        FileSystemCacheStore store = Store;
        ICacheSession session = await store.OpenSessionAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        using var source = new BlockingReadStream();
        Task<ContentHash> put = session.PutStreamAsync(source, cancellation.Token);
        await source.Started.Task;
        Task dispose = session.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => put);
        await dispose;
        Assert.Empty(Directory.GetFiles(Path.Combine(store.DirectoryPath, "content")));
        Assert.Empty(Directory.GetFiles(Path.Combine(store.DirectoryPath, "tmp")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalDrainsCancellationResistantIo(bool cancel)
    {
        FileSystemCacheStore store = Store;
        ICacheSession session = await store.OpenSessionAsync(CancellationToken.None);
        using var source = new PausedReadStream();
        using var cancellation = new CancellationTokenSource();
        Task<ContentHash> put = session.PutStreamAsync(source, cancellation.Token);
        Task dispose = Task.CompletedTask;
        try
        {
            await source.Paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
            dispose = session.DisposeAsync().AsTask();
            if (cancel)
            {
                cancellation.Cancel();
            }

            Assert.False(put.IsCompleted);
            Assert.False(dispose.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => session.GetAsync(Key("key"), CancellationToken.None));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CacheFileSystem.AcquireLockAsync(store.LockPath, exclusive: true, timeout.Token));
        }
        finally
        {
            source.Resume.TrySetResult(true);
            await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }

        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => put);
            Assert.Empty(Directory.GetFiles(Path.Combine(store.DirectoryPath, "content")));
        }
        else
        {
            ContentHash hash = await put;
            Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(store.BlobPath(hash)));
        }

        await dispose;
        Assert.Empty(Directory.GetFiles(Path.Combine(store.DirectoryPath, "tmp")));
        using var acquiredTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using FileStream maintenance = await CacheFileSystem.AcquireLockAsync(store.LockPath, exclusive: true, acquiredTimeout.Token);
    }

    [Fact]
    public async Task ExclusiveWaitersDoNotHoldSharedLocks()
    {
        FileSystemCacheStore store = Store;
        ICacheSession session = await store.OpenSessionAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<FileStream> first = CacheFileSystem.AcquireLockAsync(store.LockPath, exclusive: true, timeout.Token);
        Task<FileStream> second = CacheFileSystem.AcquireLockAsync(store.LockPath, exclusive: true, timeout.Token);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        await session.DisposeAsync();
        Task<FileStream> winner = await Task.WhenAny(first, second);
        (await winner).Dispose();
        using FileStream loser = await (winner == first ? second : first);
    }

    [Fact]
    public async Task ConcurrentOperationsOnOneSession()
    {
        await using ICacheSession session = await Store.OpenSessionAsync(CancellationToken.None);
        Task[] operations = Enumerable.Range(0, 16).Select(async i =>
        {
            ContentHash hash = await PutAsync(session, "shared");
            var entry = new CacheEntry(new[] { hash });
            await session.AddOrGetAsync(Key(i.ToString(System.Globalization.CultureInfo.InvariantCulture)), entry, CancellationToken.None);
            Assert.Equal(entry, await session.GetAsync(Key(i.ToString(System.Globalization.CultureInfo.InvariantCulture)), CancellationToken.None));
        }).ToArray();
        await Task.WhenAll(operations);
    }

    [Theory]
    [InlineData("equivalent")]
    [InlineData("conflict")]
    [InlineData("different")]
    public async Task ConcurrentProcessPublication(string mode)
    {
        string[] ids = { "a", "b", "c", "d" };
        await RunProcessesAsync(id => RemoteExecutor.Invoke(PublishWorker, _root, mode, id, new RemoteInvokeOptions { CheckExitCode = false }), ids);
        string[] results = ids.Select(id => File.ReadAllText(Path.Combine(_root, "result-" + id))).ToArray();
        Assert.Equal(mode == "different" ? ids.Length : 1, results.Count(r => r == nameof(PublishResult.Added)));
        Assert.Equal(mode == "equivalent" ? ids.Length - 1 : 0, results.Count(r => r == nameof(PublishResult.Equivalent)));
        Assert.Equal(mode == "conflict" ? ids.Length - 1 : 0, results.Count(r => r == "conflict"));
        await using ICacheSession session = await Store.OpenSessionAsync(CancellationToken.None);
        Assert.NotNull(await session.GetAsync(Key(mode == "different" ? "a" : "key"), CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentProcessReadersSeeOnlyCompletePublications()
    {
        await RunProcessesAsync(id => RemoteExecutor.Invoke(VisibilityWorker, _root, id, new RemoteInvokeOptions { CheckExitCode = false }),
            "write-a", "write-b", "read-a", "read-b");
        Assert.Equal(60, Directory.GetFiles(Path.Combine(Store.DirectoryPath, "entries")).Length);
    }

    private async Task RunProcessesAsync(Func<string, RemoteInvokeHandle> start, params string[] ids)
    {
        Directory.CreateDirectory(_root);
        var workers = new List<RemoteInvokeHandle>();
        try
        {
            foreach (string id in ids)
            {
                workers.Add(start(id));
            }

            await Task.WhenAll(ids.Select(id => WaitForFileAsync(Path.Combine(_root, "ready-" + id))));
            File.WriteAllText(Path.Combine(_root, "go"), "");
            await Task.WhenAll(workers.Select(worker => worker.Process.WaitForExitAsync())).WaitAsync(TimeSpan.FromSeconds(30));
            foreach (RemoteInvokeHandle worker in workers)
            {
                Assert.Equal(RemoteExecutor.SuccessExitCode, worker.Process.ExitCode);
            }
        }
        finally
        {
            foreach (RemoteInvokeHandle worker in workers)
            {
                if (!worker.Process.HasExited)
                {
                    worker.Process.Kill();
                    await worker.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }

                worker.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("exclusive")]
    public async Task ProcessDeathReleasesLease(string mode)
    {
        Directory.CreateDirectory(_root);
        using RemoteInvokeHandle worker = RemoteExecutor.Invoke(HoldWorker, _root, mode, new RemoteInvokeOptions { CheckExitCode = false });
        await WaitForFileAsync(Path.Combine(_root, "ready"));
        FileSystemCacheStore store = Store;
        if (mode == "shared")
        {
            await using ICacheSession concurrent = await store.OpenSessionAsync(CancellationToken.None);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CacheFileSystem.AcquireLockAsync(store.LockPath, exclusive: true, timeout.Token));
        }
        else
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.OpenSessionAsync(timeout.Token));
        }

        worker.Process.Kill();
        await worker.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        using var acquiredTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using FileStream maintenance = await CacheFileSystem.AcquireLockAsync(store.LockPath, exclusive: true, acquiredTimeout.Token);
    }

    private static int PublishWorker(string root, string mode, string id)
    {
        return RunAsync().GetAwaiter().GetResult();

        async Task<int> RunAsync()
        {
            var store = new FileSystemCacheStore(Path.Combine(root, "cache"));
            File.WriteAllText(Path.Combine(root, "ready-" + id), "");
            await WaitForFileAsync(Path.Combine(root, "go"));
            await using ICacheSession session = await store.OpenSessionAsync(CancellationToken.None);
            ContentHash hash = await PutAsync(session, mode == "conflict" ? id : "shared");
            string result;
            try
            {
                result = (await session.AddOrGetAsync(Key(mode == "different" ? id : "key"),
                    new CacheEntry(new[] { hash }), CancellationToken.None)).ToString();
            }
            catch (InvalidDataException e) when (mode == "conflict" && e.Message.Contains("Conflicting results"))
            {
                result = "conflict";
            }

            File.WriteAllText(Path.Combine(root, "result-" + id), result);
            return RemoteExecutor.SuccessExitCode;
        }
    }

    private static int VisibilityWorker(string root, string id)
    {
        return RunAsync().GetAwaiter().GetResult();

        async Task<int> RunAsync()
        {
            File.WriteAllText(Path.Combine(root, "ready-" + id), "");
            await WaitForFileAsync(Path.Combine(root, "go"));
            var store = new FileSystemCacheStore(Path.Combine(root, "cache"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using ICacheSession session = await store.OpenSessionAsync(timeout.Token);
            byte seed = (byte)id[id.Length - 1];
            byte[] bytes = Enumerable.Repeat(seed, 256 * 1024).ToArray();
            string observed = Path.Combine(root, "observed-" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (int i = 0; i < 30; i++)
            {
                Fingerprint key = Key(seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (id.StartsWith("write-", StringComparison.Ordinal))
                {
                    using var source = new MemoryStream(bytes);
                    ContentHash hash = await session.PutStreamAsync(source, timeout.Token);
                    Assert.Equal(PublishResult.Added, await session.AddOrGetAsync(key, new CacheEntry(new[] { hash }, new[] { seed }), timeout.Token));
                    if (i == 0)
                    {
                        await WaitForFileAsync(observed);
                    }
                }
                else
                {
                    CacheEntry? entry;
                    while ((entry = await session.GetAsync(key, timeout.Token)) is null)
                    {
                        await Task.Delay(10, timeout.Token);
                    }

                    Assert.Equal(new[] { seed }, entry.Payload);
                    using Stream content = await session.OpenStreamAsync(Assert.Single(entry.ContentHashes), timeout.Token);
                    using var restored = new MemoryStream();
                    await content.CopyToAsync(restored, timeout.Token);
                    Assert.Equal(bytes, restored.ToArray());
                    if (i == 0)
                    {
                        File.WriteAllText(observed, "");
                    }
                }
            }

            return RemoteExecutor.SuccessExitCode;
        }
    }

    private static int HoldWorker(string root, string mode)
    {
        var store = new FileSystemCacheStore(Path.Combine(root, "cache"));
        Directory.CreateDirectory(store.DirectoryPath);
        using FileStream lease = CacheFileSystem.AcquireLockAsync(store.LockPath, exclusive: mode == "exclusive", CancellationToken.None).GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(root, "ready"), "");
        Thread.Sleep(TimeSpan.FromSeconds(60));
        return RemoteExecutor.SuccessExitCode;
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!File.Exists(path))
        {
            await Task.Delay(25, timeout.Token);
        }
    }

    private static async Task<ContentHash> PutAsync(ICacheSession session, string text)
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await session.PutStreamAsync(source, CancellationToken.None);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class BlockingReadStream : MemoryStream
    {
        internal TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.SetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    private sealed class PausedReadStream : MemoryStream
    {
        private bool _sentContent;
        internal TaskCompletionSource<bool> Paused { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Resume { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sentContent)
            {
                _sentContent = true;
                buffer.Span[0] = 42;
                return 1;
            }

            Paused.SetResult(true);
            await Resume.Task;
            return 0;
        }
    }
}
