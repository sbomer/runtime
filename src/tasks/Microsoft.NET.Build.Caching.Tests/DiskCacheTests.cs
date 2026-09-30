// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.RemoteExecutor;
using Xunit;

namespace Microsoft.NET.Build.Caching.Tests;

[PlatformSpecific(TestPlatforms.Windows | TestPlatforms.Linux)]
public sealed class DiskCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(DiskCacheTests), Guid.NewGuid().ToString("N"));

    private static ContentHash Hash(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static CacheKey Key(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [Fact]
    public async Task ConstructionAndIndexMissDoNotCreateStorage()
    {
        var cache = new DiskCache(_root);
        Assert.False(Directory.Exists(_root));
        Assert.Null(await cache.Index.GetAsync(Key("missing")));
        await Assert.ThrowsAnyAsync<IOException>(() => cache.Cas.GetAsync(Hash("missing")).AsTask());
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyDirectoryIsRejected(string directory)
    {
        Assert.Throws<ArgumentException>(() => new DiskCache(directory));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void HashesRequireExactly32Bytes(int length)
    {
        Assert.Throws<ArgumentException>(() => new ContentHash(new byte[length]));
        Assert.Throws<ArgumentException>(() => new CacheKey(new byte[length]));
    }

    [Fact]
    public void HashValuesAreImmutableAndDistinct()
    {
        byte[] bytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        byte[] original = bytes.ToArray();
        var content = new ContentHash(bytes);
        var key = new CacheKey(bytes);
        bytes[0] = 255;
        content.ToByteArray()[1] = 255;
        key.ToByteArray()[2] = 255;
        Assert.Equal(original, content.ToByteArray());
        Assert.Equal(original, key.ToByteArray());
        Assert.Equal(Convert.ToHexString(original).ToLowerInvariant(), content.ToString());
        Assert.Equal(content.ToString(), key.ToString());
        Assert.Equal(content, new ContentHash(original));
        Assert.Equal(key, new CacheKey(original));
        Assert.Equal(content.GetHashCode(), new ContentHash(original).GetHashCode());
        Assert.Equal(key.GetHashCode(), new CacheKey(original).GetHashCode());
        Assert.True(content == new ContentHash(original));
        Assert.True(key == new CacheKey(original));
        Assert.True(content != new ContentHash(bytes));
        Assert.True(key != new CacheKey(bytes));
        Assert.False(content.Equals((object)key));
        Assert.False(key.Equals((object)content));
        Assert.Equal(new ContentHash(new byte[32]), default);
        Assert.Equal(new CacheKey(new byte[32]), default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(2097152)]
    public async Task ContentRoundTripsFromCurrentPosition(int length)
    {
        byte[] bytes = new byte[length + 11];
        new Random(42).NextBytes(bytes);
        using var source = new MemoryStream(bytes);
        source.Position = 11;
        var cache = new DiskCache(_root);
        ContentHash hash = await cache.Cas.PutAsync(source);
        Assert.Equal(new ContentHash(SHA256.HashData(bytes.AsSpan(11))), hash);
        Assert.Equal(source.Length, source.Position);
        Assert.True(source.CanRead);
        using Stream restored = await new DiskCache(_root).Cas.GetAsync(hash);
        Assert.True(restored.CanRead);
        Assert.False(restored.CanWrite);
        Assert.Equal(0, restored.Position);
        using var output = new MemoryStream();
        await restored.CopyToAsync(output);
        Assert.Equal(bytes.AsSpan(11).ToArray(), output.ToArray());
        Assert.Single(Directory.GetFiles(_root, "*.blob", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PutSupportsNonSeekableAsyncSourcesAndDoesNotDisposeThem()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("content supplied in short reads");
        using var source = new AsyncOnlyStream(bytes, chunkSize: 3);
        ContentHash hash = await new DiskCache(_root).Cas.PutAsync(source);
        Assert.Equal(new ContentHash(SHA256.HashData(bytes)), hash);
        Assert.False(source.WasDisposed);
        Assert.True(source.ReadCount > 1);
    }

    [Fact]
    public async Task PutRejectsUnreadableStreams()
    {
        using var source = new MemoryStream();
        source.Dispose();
        await Assert.ThrowsAsync<ArgumentException>(() => new DiskCache(_root).Cas.PutAsync(source).AsTask());
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task DuplicatePutVerifiesWinnerWithoutReplacingIt()
    {
        var cache = new DiskCache(_root);
        ContentHash hash = await PutAsync(cache, "content");
        string path = Assert.Single(Directory.GetFiles(_root, "*.blob", SearchOption.AllDirectories));
        File.SetLastWriteTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        Assert.Equal(hash, await PutAsync(new DiskCache(_root), "content"));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        File.WriteAllText(path, "corrupt");
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.Cas.GetAsync(hash).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => PutAsync(cache, "content"));
        Assert.Equal("corrupt", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ReturnedStreamsAreIndependentAndRemainUsable()
    {
        var cache = new DiskCache(_root);
        ContentHash hash = await PutAsync(cache, "content");
        using Stream first = await cache.Cas.GetAsync(hash);
        using Stream second = await new DiskCache(_root).Cas.GetAsync(hash);
        Assert.Equal('c', first.ReadByte());
        Assert.Equal(0, second.Position);
        second.Dispose();
        using var reader = new StreamReader(first);
        Assert.Equal("ontent", await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexIsIndependentAndPreservesWinner(bool zeroDigests)
    {
        var cache = new DiskCache(_root);
        CacheKey key = zeroDigests ? default : Key("operation");
        ContentHash winner = zeroDigests ? default : Hash("winner");
        ContentHash loser = Hash("loser");
        Assert.Equal(winner, await cache.Index.GetOrAddAsync(key, winner));
        Assert.Equal(winner, await cache.Index.GetOrAddAsync(key, winner));
        Assert.Equal(winner, await cache.Index.GetOrAddAsync(key, loser));
        Assert.Equal(winner, await new DiskCache(_root).Index.GetAsync(key));
        Assert.Null(await cache.Index.GetAsync(Key("other")));
        Assert.Empty(Directory.GetFiles(_root, "*.blob", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(_root, "*.entry", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("extended")]
    [InlineData("value")]
    [InlineData("key")]
    [InlineData("checksum")]
    public async Task CorruptIndexIsNotAMissAndIsNotReplaced(string corruption)
    {
        var cache = new DiskCache(_root);
        CacheKey key = Key("key");
        await cache.Index.GetOrAddAsync(key, Hash("value"));
        string path = Assert.Single(Directory.GetFiles(_root, "*.entry", SearchOption.AllDirectories));
        byte[] record = File.ReadAllBytes(path);
        switch (corruption)
        {
            case "truncated":
                Array.Resize(ref record, record.Length - 1);
                break;
            case "extended":
                Array.Resize(ref record, record.Length + 1);
                break;
            case "value":
                record[32] ^= 1;
                break;
            case "key":
                record[0] ^= 1;
                SHA256.HashData(record.AsSpan(0, 64)).CopyTo(record, 64);
                break;
            case "checksum":
                record[64] ^= 1;
                break;
        }

        File.WriteAllBytes(path, record);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.Index.GetAsync(key).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.Index.GetOrAddAsync(key, Hash("replacement")).AsTask());
        Assert.Equal(record, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task EveryTruncatedIndexRecordIsRejected()
    {
        var cache = new DiskCache(_root);
        CacheKey key = Key("key");
        await cache.Index.GetOrAddAsync(key, Hash("value"));
        string path = Assert.Single(Directory.GetFiles(_root, "*.entry", SearchOption.AllDirectories));
        byte[] record = File.ReadAllBytes(path);
        for (int length = 0; length < record.Length; length++)
        {
            File.WriteAllBytes(path, record.AsSpan(0, length).ToArray());
            await Assert.ThrowsAsync<InvalidDataException>(() => cache.Index.GetAsync(key).AsTask());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCanceledOperationsDoNotAccessStorage(bool existing)
    {
        var cache = new DiskCache(_root);
        if (existing)
        {
            await PutAsync(cache, "content");
            await cache.Index.GetOrAddAsync(Key("key"), Hash("content"));
        }

        var token = new CancellationToken(canceled: true);
        using var source = new AsyncOnlyStream(Encoding.UTF8.GetBytes("content"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.Cas.PutAsync(source, token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.Cas.GetAsync(Hash("content"), token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.Index.GetAsync(Key("key"), token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.Index.GetOrAddAsync(Key("key"), Hash("content"), token).AsTask());
        Assert.Equal(0, source.ReadCount);
        Assert.Equal(existing, Directory.Exists(_root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompletePutDoesNotPublishAndCleansUp(bool cancel)
    {
        var cache = new DiskCache(_root);
        using var cancellation = new CancellationTokenSource();
        using var source = new AsyncOnlyStream(new byte[100], chunkSize: 7)
        {
            FailAfterFirstRead = !cancel,
            AfterRead = cancel ? cancellation.Cancel : null
        };
        Task<ContentHash> put = cache.Cas.PutAsync(source, cancellation.Token).AsTask();
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => put);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => put);
        }

        Assert.False(source.WasDisposed);
        Assert.Empty(Directory.GetFiles(_root, "*.blob", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task InProgressPutIsNotVisible()
    {
        var cache = new DiskCache(_root);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] bytes = Encoding.UTF8.GetBytes("complete content");
        using var source = new AsyncOnlyStream(bytes)
        {
            BeforeRead = async () =>
            {
                started.TrySetResult();
                await release.Task;
            }
        };
        Task<ContentHash> put = cache.Cas.PutAsync(source).AsTask();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(put.IsCompleted);
            await Assert.ThrowsAnyAsync<IOException>(() => new DiskCache(_root).Cas.GetAsync(new ContentHash(SHA256.HashData(bytes))).AsTask());
            Assert.Empty(Directory.GetFiles(_root, "*.blob", SearchOption.AllDirectories));
        }
        finally
        {
            release.TrySetResult();
            await put.WaitAsync(TimeSpan.FromSeconds(30));
        }

        Assert.Single(Directory.GetFiles(_root, "*.blob", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task SameInstanceSupportsConcurrentOperations()
    {
        var cache = new DiskCache(_root);
        ContentHash[] hashes = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => PutAsync(cache, "same content")));
        Assert.All(hashes, hash => Assert.Equal(Hash("same content"), hash));
        CacheKey key = Key("race");
        ContentHash[] winners = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => cache.Index.GetOrAddAsync(key, Hash(i.ToString())).AsTask()));
        Assert.All(winners, winner => Assert.Equal(winners[0], winner));
        Assert.Equal(winners[0], await cache.Index.GetAsync(key));
        Assert.Single(Directory.GetFiles(_root, "*.blob", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(_root, "*.entry", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicPublicationNeverReplacesACompetingWinner(bool longPaths)
    {
        string directory = longPaths ? Path.Combine(_root, new string('a', 100), new string('b', 100), new string('c', 100)) : _root;
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, "winner");
        string[] sources = Enumerable.Range(0, 16).Select(i => Path.Combine(directory, "source-" + i)).ToArray();
        foreach (string source in sources)
        {
            File.WriteAllText(source, source);
        }

        using var gate = new ManualResetEventSlim();
        Task<bool>[] publishers = sources.Select(source => Task.Run(() =>
        {
            gate.Wait();
            return DiskCacheFileSystem.Publish(source, destination);
        })).ToArray();
        gate.Set();
        bool[] results = await Task.WhenAll(publishers).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results, published => published);
        Assert.Equal(sources[Array.IndexOf(results, true)], File.ReadAllText(destination));
        for (int i = 0; i < sources.Length; i++)
        {
            if (!results[i])
            {
                Assert.Equal(sources[i], File.ReadAllText(sources[i]));
            }
        }
    }

    [Theory]
    [InlineData(@"C:\", @"\\?\C:\", 259)]
    [InlineData(@"C:\", @"\\?\C:\", 260)]
    [InlineData(@"C:\", @"\\?\C:\", 300)]
    [InlineData(@"\\server\share\", @"\\?\UNC\server\share\", 259)]
    [InlineData(@"\\server\share\", @"\\?\UNC\server\share\", 260)]
    [InlineData(@"\\server\share\", @"\\?\UNC\server\share\", 300)]
    [InlineData(@"\\?\C:\", @"\\?\C:\", 300)]
    [InlineData(@"\\?\UNC\server\share\", @"\\?\UNC\server\share\", 300)]
    [InlineData(@"\\.\C:\", @"\\.\C:\", 300)]
    [InlineData(@"\??\C:\", @"\??\C:\", 300)]
    public void WindowsPublicationPathsPreserveShortAndDevicePaths(string prefix, string extendedPrefix, int length)
    {
        string directory = new string('a', 100) + @"\";
        string suffix = directory + new string('b', length - prefix.Length - directory.Length - ".blob".Length) + ".blob";
        string path = prefix + suffix;
        string expected = length < 260 ? path : extendedPrefix + suffix;
        string actual = DiskCacheFileSystem.GetWindowsPublicationPath(path);
        Assert.Equal(expected, actual);
        Assert.Same(actual, DiskCacheFileSystem.GetWindowsPublicationPath(actual));
        if (expected == path)
        {
            Assert.Same(path, actual);
        }
    }

    [Fact]
    public async Task ContentAndIndexSupportLongPaths()
    {
        string directory = Path.Combine(_root, new string('a', 100), new string('b', 100), new string('c', 100));
        var cache = new DiskCache(directory);
        ContentHash hash = await PutAsync(cache, "content");
        CacheKey key = Key("key");
        Assert.Equal(hash, await cache.Index.GetOrAddAsync(key, hash));
        Assert.Equal(hash, await PutAsync(new DiskCache(directory), "content"));
        Assert.Equal(hash, await cache.Index.GetOrAddAsync(key, Hash("loser")));
        Assert.Equal(hash, await new DiskCache(directory).Index.GetAsync(key));
        using Stream restored = await new DiskCache(directory).Cas.GetAsync(hash);
        using var reader = new StreamReader(restored);
        Assert.Equal("content", await reader.ReadToEndAsync());
        Assert.True(Assert.Single(Directory.GetFiles(directory, "*.blob", SearchOption.AllDirectories)).Length >= 260);
        Assert.True(Assert.Single(Directory.GetFiles(directory, "*.entry", SearchOption.AllDirectories)).Length >= 260);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void PublicationFailuresAreNotReportedAsExistingEntries()
    {
        Directory.CreateDirectory(_root);
        Assert.Throws<IOException>(() => DiskCacheFileSystem.Publish(
            Path.Combine(_root, "missing"), Path.Combine(_root, "destination")));
        Assert.False(File.Exists(Path.Combine(_root, "destination")));
    }

    [ConditionalTheory(typeof(RemoteExecutor), nameof(RemoteExecutor.IsSupported))]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentProcessesPublishCompleteContentAndAgreeOnWinner(bool conflict)
    {
        Directory.CreateDirectory(_root);
        var workers = new List<RemoteInvokeHandle>();
        try
        {
            foreach (string id in new[] { "a", "b", "c", "d" })
            {
                workers.Add(RemoteExecutor.Invoke(PublishWorker, _root, conflict ? "conflict" : "same", id));
            }

            await Task.WhenAll(new[] { "a", "b", "c", "d" }.Select(id => WaitForFileAsync(Path.Combine(_root, "ready-" + id))));
            File.WriteAllText(Path.Combine(_root, "go"), "");
            await Task.WhenAll(workers.Select(worker => worker.Process.WaitForExitAsync())).WaitAsync(TimeSpan.FromSeconds(60));
            Assert.All(workers, worker => Assert.Equal(RemoteExecutor.SuccessExitCode, worker.Process.ExitCode));
            string[] winners = new[] { "a", "b", "c", "d" }.Select(id => File.ReadAllText(Path.Combine(_root, "result-" + id))).ToArray();
            Assert.All(winners, winner => Assert.Equal(winners[0], winner));
            var cache = new DiskCache(_root);
            Assert.Equal(winners[0], (await cache.Index.GetAsync(Key("race")))!.Value.ToString());
            Assert.Equal(conflict ? 4 : 1, Directory.GetFiles(_root, "*.blob", SearchOption.AllDirectories).Length);
            Assert.Equal(65, Directory.GetFiles(_root, "*.entry", SearchOption.AllDirectories).Length);
            Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
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

    private static int PublishWorker(string root, string mode, string id)
    {
        RunAsync().GetAwaiter().GetResult();
        return RemoteExecutor.SuccessExitCode;

        async Task RunAsync()
        {
            var cache = new DiskCache(root);
            File.WriteAllText(Path.Combine(root, "ready-" + id), "");
            await WaitForFileAsync(Path.Combine(root, "go"));
            string text = new string(mode == "same" ? 'x' : id[0], 100000);
            ContentHash hash = await PutAsync(cache, text);
            ContentHash winner = await cache.Index.GetOrAddAsync(Key("race"), hash);
            using (Stream content = await cache.Cas.GetAsync(winner))
            {
                using var reader = new StreamReader(content);
                Assert.Equal(winner, Hash(await reader.ReadToEndAsync()));
            }

            for (int i = 0; i < 16; i++)
            {
                CacheKey key = Key(id + i);
                Assert.Equal(hash, await cache.Index.GetOrAddAsync(key, hash));
                Assert.Equal(hash, await new DiskCache(root).Index.GetAsync(key));
            }

            File.WriteAllText(Path.Combine(root, "result-" + id), winner.ToString());
        }
    }

    [Theory]
    [PlatformSpecific(TestPlatforms.Linux)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewDirectoriesArePrivateAndExistingPermissionsArePreserved(bool preexisting)
    {
        UnixFileMode shared = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
        if (preexisting)
        {
            Directory.CreateDirectory(_root);
            File.SetUnixFileMode(_root, shared);
        }

        var cache = new DiskCache(_root);
        await PutAsync(cache, "content");
        await cache.Index.GetOrAddAsync(Key("key"), Hash("content"));
        UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Assert.Equal(preexisting ? shared : ownerOnly, File.GetUnixFileMode(_root));
        foreach (string directory in Directory.GetDirectories(_root, "*", SearchOption.AllDirectories))
        {
            Assert.Equal(ownerOnly, File.GetUnixFileMode(directory));
        }

        foreach (string file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
    }

    [Fact]
    [PlatformSpecific(TestPlatforms.Linux)]
    public async Task LinkedRootsAreRejected()
    {
        Directory.CreateDirectory(_root);
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        string link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, target);
        var cache = new DiskCache(link);
        await Assert.ThrowsAsync<IOException>(() => PutAsync(cache, "content"));
        await Assert.ThrowsAsync<IOException>(() => cache.Index.GetAsync(Key("key")).AsTask());
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    [Theory]
    [PlatformSpecific(TestPlatforms.Linux)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkedPublishedFilesAreRejected(bool index)
    {
        var cache = new DiskCache(_root);
        ContentHash hash = await PutAsync(cache, "content");
        CacheKey key = Key("key");
        await cache.Index.GetOrAddAsync(key, hash);
        string path = Directory.GetFiles(_root, index ? "*.entry" : "*.blob", SearchOption.AllDirectories).Single();
        string original = path + ".original";
        File.Move(path, original);
        File.CreateSymbolicLink(path, original);
        if (index)
        {
            await Assert.ThrowsAsync<IOException>(() => cache.Index.GetAsync(key).AsTask());
            await Assert.ThrowsAsync<IOException>(() => cache.Index.GetOrAddAsync(key, hash).AsTask());
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => cache.Cas.GetAsync(hash).AsTask());
            await Assert.ThrowsAsync<IOException>(() => PutAsync(cache, "content"));
        }
    }

    [Fact]
    public async Task StorageFailuresAreNotIndexMisses()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "DiskCache.v1"), "not a directory");
        var cache = new DiskCache(_root);
        await Assert.ThrowsAnyAsync<IOException>(() => cache.Index.GetAsync(Key("key")).AsTask());
        await Assert.ThrowsAnyAsync<IOException>(() => cache.Index.GetOrAddAsync(Key("key"), Hash("value")).AsTask());
    }

    private static async Task<ContentHash> PutAsync(DiskCache cache, string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await cache.Cas.PutAsync(stream);
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!File.Exists(path))
        {
            await Task.Delay(25, timeout.Token);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class AsyncOnlyStream(byte[] bytes, int chunkSize = 65536) : Stream
    {
        private readonly MemoryStream _source = new(bytes);
        internal int ReadCount { get; private set; }
        internal bool WasDisposed { get; private set; }
        internal bool FailAfterFirstRead { get; init; }
        internal Action? AfterRead { get; init; }
        internal Func<Task>? BeforeRead { get; init; }
        public override bool CanRead => !WasDisposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (BeforeRead is not null)
            {
                await BeforeRead();
            }

            if (FailAfterFirstRead && ReadCount != 0)
            {
                throw new IOException("Source read failed.");
            }

            ReadCount++;
            int read = await _source.ReadAsync(buffer, offset, Math.Min(count, chunkSize), cancellationToken);
            AfterRead?.Invoke();
            return read;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Synchronous reads are not supported.");
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            if (disposing)
            {
                _source.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
