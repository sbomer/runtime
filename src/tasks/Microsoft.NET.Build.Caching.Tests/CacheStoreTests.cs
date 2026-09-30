// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Sessions;
using Microsoft.DotNet.RemoteExecutor;
using Xunit;

namespace Microsoft.NET.Build.Caching.Tests;

[PlatformSpecific(TestPlatforms.Windows | TestPlatforms.Linux)]
public sealed class CacheStoreTests : IDisposable
{
    private static readonly Context s_context = new Context(NullLogger.Instance);
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(CacheStoreTests), Guid.NewGuid().ToString("N"));

    private static StrongFingerprint Key(string text) => new StrongFingerprint(
        new Fingerprint(HashInfoLookup.GetContentHasher(HashType.SHA256).GetContentHash(Encoding.UTF8.GetBytes(text)).ToHashByteArray()),
        new Selector(HashInfoLookup.Find(HashType.SHA256).EmptyHash));

    [Fact]
    public void ConstructionDoesNotAccessStorage()
    {
        using ICache cache = CacheStore.Create(_root);
        Assert.IsType<ConcurrentLocalCache>(cache);
        Assert.False(Directory.Exists(_root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("cached output")]
    [InlineData("non-ascii \u00e9 \ud83d\ude00")]
    public async Task ContentAndMemoizationRoundTripAcrossCacheInstances(string text)
    {
        ContentHash hash;
        ContentHashList list;
        Guid id;
        using (ICache cache = await OpenAsync(_root))
        using (ICacheSession session = await OpenSessionAsync(cache))
        {
            id = cache.Id;
            GetContentHashListResult miss = await session.GetContentHashListAsync(s_context, Key("key"), default);
            Succeeded(miss);
            Assert.Null(miss.ContentHashListWithDeterminism.ContentHashList);
            hash = await PutAsync(session, text);
            list = new ContentHashList(new[] { hash, hash }, new byte[] { 1, 2 });
            AddOrGetContentHashListResult added = await session.AddOrGetContentHashListAsync(s_context, Key("key"), WithDeterminism(list), default);
            Succeeded(added);
            Assert.Null(added.ContentHashListWithDeterminism.ContentHashList);
            AddOrGetContentHashListResult equivalent = await session.AddOrGetContentHashListAsync(s_context, Key("key"), WithDeterminism(list), default);
            Succeeded(equivalent);
            Assert.Null(equivalent.ContentHashListWithDeterminism.ContentHashList);
            Succeeded(await session.ShutdownAsync(s_context));
            Succeeded(await cache.ShutdownAsync(s_context));
        }

        using ICache reopened = await OpenAsync(_root);
        Assert.Equal(id, reopened.Id);
        using ICacheSession reader = await OpenSessionAsync(reopened);
        GetContentHashListResult hit = await reader.GetContentHashListAsync(s_context, Key("key"), default);
        Succeeded(hit);
        Assert.Equal(list, hit.ContentHashListWithDeterminism.ContentHashList);
        OpenStreamResult opened = await reader.OpenStreamAsync(s_context, hash, default);
        Succeeded(opened);
        using (Stream stream = Assert.IsAssignableFrom<Stream>(opened.Stream))
        using (var textReader = new StreamReader(stream))
        {
            Assert.False(stream.CanWrite);
            Assert.Equal(text, await textReader.ReadToEndAsync());
        }

        string output = Path.Combine(_root, "output");
        Succeeded(await reader.PlaceFileAsync(s_context, hash, new AbsolutePath(output),
            FileAccessMode.Write, FileReplacementMode.ReplaceExisting, FileRealizationMode.Copy, default));
        Assert.Equal(text, File.ReadAllText(output));
        File.WriteAllText(output, "changed");
        Succeeded(await reader.PlaceFileAsync(s_context, hash, new AbsolutePath(output),
            FileAccessMode.Write, FileReplacementMode.ReplaceExisting, FileRealizationMode.Copy, default));
        Assert.Equal(text, File.ReadAllText(output));
    }

    [Theory]
    [InlineData("content")]
    [InlineData("order")]
    [InlineData("duplicates")]
    [InlineData("payload")]
    [InlineData("null-empty")]
    public async Task ConflictsPreserveWinningValue(string difference)
    {
        using ICache cache = await OpenAsync(_root);
        using ICacheSession session = await OpenSessionAsync(cache);
        ContentHash first = await PutAsync(session, "first");
        ContentHash second = await PutAsync(session, "second");
        var winner = new ContentHashList(new[] { first, second });
        ContentHashList candidate = difference switch
        {
            "content" => new ContentHashList(new[] { second, second }),
            "order" => new ContentHashList(new[] { second, first }),
            "duplicates" => new ContentHashList(new[] { first, second, second }),
            "payload" => new ContentHashList(new[] { first, second }, new byte[] { 1 }),
            "null-empty" => new ContentHashList(new[] { first, second }, Array.Empty<byte>()),
            _ => throw new InvalidOperationException(difference)
        };
        Succeeded(await session.AddOrGetContentHashListAsync(s_context, Key("key"), WithDeterminism(winner), default));
        AddOrGetContentHashListResult conflict = await session.AddOrGetContentHashListAsync(s_context, Key("key"), WithDeterminism(candidate), default);
        Assert.Equal(AddOrGetContentHashListResult.ResultCode.InvalidToolDeterminismError, conflict.Code);
        Assert.Equal(winner, conflict.ContentHashListWithDeterminism.ContentHashList);
        GetContentHashListResult hit = await session.GetContentHashListAsync(s_context, Key("key"), default);
        Succeeded(hit);
        Assert.Equal(winner, hit.ContentHashListWithDeterminism.ContentHashList);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingContentAndVerifiedCopyCorruptionRemainErrors(bool corrupt)
    {
        using ICache cache = await OpenAsync(_root);
        using ICacheSession session = await OpenSessionAsync(cache);
        ContentHash hash = await PutAsync(session, "content");
        var list = new ContentHashList(new[] { hash });
        Succeeded(await session.AddOrGetContentHashListAsync(s_context, Key("key"), WithDeterminism(list), default));
        string blob = Directory.GetFiles(_root, "*.blob", SearchOption.AllDirectories).Single();
        string record = Directory.GetFiles(_root, "*.entry", SearchOption.AllDirectories).Single();
        byte[] original = File.ReadAllBytes(record);
        if (corrupt)
        {
            File.WriteAllText(blob, "corrupt");
            Succeeded(await session.GetContentHashListAsync(s_context, Key("key"), default));
            OpenStreamResult opened = await session.OpenStreamAsync(s_context, hash, default);
            Succeeded(opened);
            using (Stream stream = Assert.IsAssignableFrom<Stream>(opened.Stream))
            using (var reader = new StreamReader(stream))
            {
                Assert.Equal("corrupt", await reader.ReadToEndAsync());
            }

            PlaceFileResult placed = await session.PlaceFileAsync(s_context, hash, new AbsolutePath(Path.Combine(_root, "output")),
                FileAccessMode.Write, FileReplacementMode.ReplaceExisting, FileRealizationMode.Copy, default);
            Assert.Equal(PlaceFileResult.ResultCode.NotPlacedContentHashMismatch, placed.Code);
        }
        else
        {
            File.Delete(blob);
            GetContentHashListResult hit = await session.GetContentHashListAsync(s_context, Key("key"), default);
            Assert.False(hit.Succeeded);
            Assert.Contains("Missing referenced content", hit.ErrorMessage);
            AddOrGetContentHashListResult publication = await session.AddOrGetContentHashListAsync(s_context, Key("key"), WithDeterminism(list), default);
            Assert.False(publication.Succeeded);
            Assert.Contains("Missing referenced content", publication.ErrorMessage);
        }

        Assert.Equal(original, File.ReadAllBytes(record));
    }

    [Fact]
    public async Task ReturnedStreamOutlivesCacheAndSession()
    {
        using ICache cache = await OpenAsync(_root);
        using ICacheSession session = await OpenSessionAsync(cache);
        ContentHash hash = await PutAsync(session, "content");
        OpenStreamResult opened = await session.OpenStreamAsync(s_context, hash, default);
        Succeeded(opened);
        using Stream stream = Assert.IsAssignableFrom<Stream>(opened.Stream);
        session.Dispose();
        cache.Dispose();
        using var reader = new StreamReader(stream);
        Assert.Equal("content", await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentProcessesCanPublish(bool conflict)
    {
        Directory.CreateDirectory(_root);
        string mode = conflict ? "conflict" : "equivalent";
        using RemoteInvokeHandle first = RemoteExecutor.Invoke(PublishWorker, _root, mode, "a");
        using RemoteInvokeHandle second = RemoteExecutor.Invoke(PublishWorker, _root, mode, "b");
        try
        {
            await Task.WhenAll(WaitForFileAsync(Path.Combine(_root, "ready-a")), WaitForFileAsync(Path.Combine(_root, "ready-b")));
            File.WriteAllText(Path.Combine(_root, "go"), "");
            await Task.WhenAll(first.Process.WaitForExitAsync(), second.Process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(RemoteExecutor.SuccessExitCode, first.Process.ExitCode);
            Assert.Equal(RemoteExecutor.SuccessExitCode, second.Process.ExitCode);
            string[] results = { File.ReadAllText(Path.Combine(_root, "result-a")), File.ReadAllText(Path.Combine(_root, "result-b")) };
            Assert.Equal(conflict ? 1 : 2, results.Count(value => value == "success"));
            Assert.Equal(conflict ? 1 : 0, results.Count(value => value == "conflict"));
        }
        finally
        {
            foreach (RemoteInvokeHandle worker in new[] { first, second })
            {
                if (!worker.Process.HasExited)
                {
                    worker.Process.Kill();
                    await worker.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
        }
    }

    private static int PublishWorker(string root, string mode, string id)
    {
        return RunAsync().GetAwaiter().GetResult();

        async Task<int> RunAsync()
        {
            File.WriteAllText(Path.Combine(root, "ready-" + id), "");
            await WaitForFileAsync(Path.Combine(root, "go"));
            using ICache cache = await OpenAsync(root);
            using ICacheSession session = await OpenSessionAsync(cache);
            ContentHash hash = await PutAsync(session, mode == "conflict" ? id : "shared");
            AddOrGetContentHashListResult result = await session.AddOrGetContentHashListAsync(s_context, Key("key"), WithDeterminism(new ContentHashList(new[] { hash })), default);
            if (!result.Succeeded)
            {
                Assert.Equal(AddOrGetContentHashListResult.ResultCode.InvalidToolDeterminismError, result.Code);
            }

            File.WriteAllText(Path.Combine(root, "result-" + id), result.Succeeded ? "success" : "conflict");
            return RemoteExecutor.SuccessExitCode;
        }
    }

    private static async Task<ICache> OpenAsync(string root)
    {
        ICache cache = CacheStore.Create(root);
        BoolResult started = await cache.StartupAsync(s_context);
        if (!started.Succeeded)
        {
            cache.Dispose();
        }

        Succeeded(started);
        return cache;
    }

    private static async Task<ICacheSession> OpenSessionAsync(ICache cache)
    {
        CreateSessionResult<ICacheSession> created = cache.CreateSession(s_context, "test", ImplicitPin.PutAndGet);
        Succeeded(created);
        ICacheSession session = Assert.IsAssignableFrom<ICacheSession>(created.Session);
        Succeeded(await session.StartupAsync(s_context));
        return session;
    }

    private static async Task<ContentHash> PutAsync(ICacheSession session, string text)
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(text));
        PutResult result = await session.PutStreamAsync(s_context, HashType.SHA256, source, default);
        Succeeded(result);
        return result.ContentHash;
    }

    private static ContentHashListWithDeterminism WithDeterminism(ContentHashList list) => new ContentHashListWithDeterminism(list, CacheDeterminism.Tool);
    private static void Succeeded(ResultBase result) => Assert.True(result.Succeeded, result.ToString());

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
}
