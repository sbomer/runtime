// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
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
using Microsoft.Build.Framework;

namespace Microsoft.NET.Build.Caching;

/// <summary>Executes a deterministic operation with explicitly declared inputs and outputs and optional caching.</summary>
/// <remarks>This prototype requires task authors to declare every dependency and replayable output property.</remarks>
public abstract class DeclaredIOTask : Microsoft.Build.Utilities.Task, ICancelableTask
{
    private const int FormatVersion = 3;
    private const int HashSize = 32;
    private static readonly Context s_context = new(NullLogger.Instance);
    private readonly object _cancellationGate = new();
    private CancellationTokenSource? _cancellation;
    private int _cancellers;
    private bool _cancelRequested;
    private int _executing;

    /// <summary>Gets or sets the directory identifying the shared cache.</summary>
    /// <remarks>This parameter is required even when caching is disabled. It has no directory fallback.</remarks>
    [Required]
    public string CacheDirectory { get; set; } = string.Empty;

    /// <summary>Gets or sets a value that indicates whether cache lookup and publication are enabled.</summary>
    /// <value><see langword="true"/> to use the cache; otherwise, <see langword="false"/>. The default is <see langword="false"/>.</value>
    public bool CacheEnabled { get; set; }

    /// <summary>Declares the operation's input files, output files, settings, and output property accessors.</summary>
    /// <param name="declaration">The declaration builder, which is sealed when this method returns.</param>
    protected abstract void DescribeOperation(TaskDeclaration declaration);

    /// <summary>Performs the operation without reading or publishing cached results.</summary>
    /// <param name="cancellationToken">The token used to cancel execution.</param>
    /// <returns><see langword="true"/> if execution succeeds; otherwise, <see langword="false"/>.</returns>
    protected abstract bool ExecuteCore(CancellationToken cancellationToken);

    /// <summary>Executes or replays the declared operation.</summary>
    /// <returns><see langword="true"/> if the operation succeeds; otherwise, <see langword="false"/>.</returns>
    public sealed override bool Execute()
    {
        if (Interlocked.CompareExchange(ref _executing, 1, 0) != 0)
        {
            throw new InvalidOperationException(SR.ConcurrentExecution);
        }

        IBuildEngine originalEngine = BuildEngine;
        TrackingBuildEngine engine = TrackingBuildEngine.Create(originalEngine);
        var cancellation = new CancellationTokenSource();
        lock (_cancellationGate)
        {
            _cancellation = cancellation;
            if (_cancelRequested)
            {
                cancellation.Cancel();
            }
        }

        BuildEngine = engine;
        try
        {
            CancellationToken token = cancellation.Token;
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(CacheDirectory))
            {
                Log.LogError(SR.CacheDirectoryRequired);
                return false;
            }

            string workingDirectory = Environment.CurrentDirectory;
            string cacheDirectory = Path.GetFullPath(Path.Combine(workingDirectory, CacheDirectory));
            var declaration = new TaskDeclaration();
            DescribeOperation(declaration);
            declaration.Seal(workingDirectory);
            if (engine.HasErrors || Log.HasLoggedErrors)
            {
                return false;
            }

            foreach (string input in declaration.Inputs)
            {
                if (!File.Exists(input))
                {
                    throw new FileNotFoundException(null, input);
                }
            }

            if (!CacheEnabled)
            {
                Log.LogMessage(MessageImportance.Low, SR.CacheDisabled);
                return RunOperation(declaration, engine, token);
            }

#if NETFRAMEWORK
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException(SR.UnsupportedFrameworkPlatform);
            }
#endif
            foreach (string output in declaration.Outputs)
            {
                StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (output.Equals(cacheDirectory, comparison) || output.StartsWith(cacheDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
                {
                    throw new ArgumentException(SR.Format(SR.OutputInsideCache, output));
                }
            }

            StrongFingerprint key = Fingerprint(declaration, workingDirectory, token);
            using var cache = new CacheLifetime(cacheDirectory);
            using (var lookup = new SessionLifetime(cache.Cache))
            {
                GetContentHashListResult result = lookup.Session.GetContentHashListAsync(s_context, key, token).GetAwaiter().GetResult();
                Check(result, token);
                ContentHashList? hashes = result.ContentHashListWithDeterminism.ContentHashList;
                if (hashes is not null)
                {
                    Restore(lookup.Session, declaration, hashes, token);
                    Log.LogMessage(MessageImportance.Low, SR.CacheHit);
                    return !engine.HasErrors;
                }
            }

            Log.LogMessage(MessageImportance.Low, SR.CacheMiss);
            if (!RunOperation(declaration, engine, token))
            {
                return false;
            }

            if (engine.HasWarnings)
            {
                Log.LogMessage(MessageImportance.Low, SR.CacheWarningBypass);
                return true;
            }

            using (var publication = new SessionLifetime(cache.Cache))
            {
                if (!Publish(publication.Session, key, declaration, engine, token) && !engine.HasErrors)
                {
                    Log.LogMessage(MessageImportance.Low, SR.CacheWarningBypass);
                }
            }

            token.ThrowIfCancellationRequested();
            return !engine.HasErrors;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Log.LogMessage(MessageImportance.Low, SR.TaskCancelled);
            return false;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or CacheException)
        {
            Log.LogErrorFromException(error, showStackTrace: false);
            return false;
        }
        finally
        {
            BuildEngine = originalEngine;
            lock (_cancellationGate)
            {
                _cancellation = null;
                while (_cancellers != 0)
                {
                    Monitor.Wait(_cancellationGate);
                }
            }

            cancellation.Dispose();
            Volatile.Write(ref _executing, 0);
        }
    }

    /// <summary>Requests cancellation of the current or upcoming execution.</summary>
    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (_cancellationGate)
        {
            _cancelRequested = true;
            cancellation = _cancellation;
            if (cancellation is null)
            {
                return;
            }

            _cancellers++;
        }

        try
        {
            cancellation.Cancel();
        }
        finally
        {
            lock (_cancellationGate)
            {
                _cancellers--;
                Monitor.PulseAll(_cancellationGate);
            }
        }
    }

    private bool RunOperation(TaskDeclaration declaration, TrackingBuildEngine engine, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bool success = ExecuteCore(token);
        token.ThrowIfCancellationRequested();
        if (!success || engine.HasErrors || Log.HasLoggedErrors)
        {
            return false;
        }

        foreach (string output in declaration.Outputs)
        {
            token.ThrowIfCancellationRequested();
            ReadMetadata(output);
        }

        return true;
    }

    private StrongFingerprint Fingerprint(TaskDeclaration declaration, string workingDirectory, CancellationToken token)
    {
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data);
        writer.Write(FormatVersion);
        TaskValueCodec.WriteString(writer, GetType().FullName ?? throw new NotSupportedException(SR.Format(SR.TaskTypeNameRequired, GetType())));
        writer.Write(GetType().Module.ModuleVersionId.ToByteArray());
        writer.Write(typeof(DeclaredIOTask).Module.ModuleVersionId.ToByteArray());
        TaskValueCodec.WriteString(writer, workingDirectory);
        TaskValueCodec.WriteString(writer, RuntimeInformation.FrameworkDescription);
        TaskValueCodec.WriteString(writer, RuntimeInformation.OSDescription);
        TaskValueCodec.WriteString(writer, RuntimeInformation.ProcessArchitecture.ToString());
        TaskValueCodec.WriteString(writer, CultureInfo.CurrentCulture.Name);
        TaskValueCodec.WriteString(writer, CultureInfo.CurrentUICulture.Name);
        TaskValueCodec.WriteString(writer, TimeZoneInfo.Local.ToSerializedString());
        writer.Write(declaration.Inputs.Length);
        foreach (string input in declaration.Inputs)
        {
            token.ThrowIfCancellationRequested();
            TaskValueCodec.WriteString(writer, input);
            using var source = File.OpenRead(input);
            writer.Write(Hash(source, token));
        }

        writer.Write(declaration.Outputs.Length);
        foreach (string output in declaration.Outputs)
        {
            TaskValueCodec.WriteString(writer, output);
        }

        writer.Write(declaration.Values.Length);
        foreach (var pair in declaration.Values)
        {
            TaskValueCodec.WriteString(writer, pair.Key);
            writer.Write(pair.Value.Length);
            writer.Write(pair.Value);
        }

        writer.Write(declaration.OutputValues.Length);
        foreach (TaskDeclaration.OutputValue output in declaration.OutputValues)
        {
            TaskValueCodec.WriteString(writer, output.Name);
            output.Codec.WriteSchema(writer);
        }

        data.Position = 0;
        return new StrongFingerprint(new Fingerprint(Hash(data, token)), new Selector(HashInfoLookup.Find(HashType.SHA256).EmptyHash));
    }

    private static byte[] Hash(Stream stream, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using SHA256 hash = SHA256.Create();
        var buffer = new byte[65536];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            token.ThrowIfCancellationRequested();
            hash.TransformBlock(buffer, 0, read, buffer, 0);
        }

        token.ThrowIfCancellationRequested();
        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return hash.Hash ?? throw new InvalidOperationException(SR.InvalidManifest);
    }

    private static bool Publish(ICacheSession session, StrongFingerprint key, TaskDeclaration declaration, TrackingBuildEngine engine, CancellationToken token)
    {
        var hashes = new ContentHash[declaration.Outputs.Length + 1];
        using var manifest = new MemoryStream();
        using var writer = new BinaryWriter(manifest, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatVersion);
        writer.Write(declaration.Outputs.Length);
        for (int i = 0; i < declaration.Outputs.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            string path = declaration.Outputs[i];
            (bool readOnly, int mode) = ReadMetadata(path);
            PutResult put = session.PutFileAsync(s_context, HashType.SHA256, new AbsolutePath(path), FileRealizationMode.Copy, token).GetAwaiter().GetResult();
            Check(put, token);
            hashes[i + 1] = put.ContentHash;
            TaskValueCodec.WriteString(writer, path);
            writer.Write(put.ContentHash.ToHashByteArray());
            writer.Write(readOnly);
            writer.Write(mode);
        }

        writer.Write(declaration.OutputValues.Length);
        foreach (TaskDeclaration.OutputValue output in declaration.OutputValues)
        {
            token.ThrowIfCancellationRequested();
            TaskValueCodec.WriteString(writer, output.Name);
            byte[] bytes = output.Capture();
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        if (engine.HasErrors || engine.HasWarnings)
        {
            return false;
        }

        manifest.Position = 0;
#pragma warning disable CA2025 // GetResult synchronously waits for the put before the manifest stream is disposed.
        PutResult manifestPut = session.PutStreamAsync(s_context, HashType.SHA256, manifest, token).GetAwaiter().GetResult();
#pragma warning restore CA2025
        Check(manifestPut, token);
        hashes[0] = manifestPut.ContentHash;
        Check(session.AddOrGetContentHashListAsync(s_context, key, new ContentHashListWithDeterminism(new ContentHashList(hashes), CacheDeterminism.Tool), token).GetAwaiter().GetResult(), token);
        return true;
    }

    private static void Restore(ICacheSession session, TaskDeclaration declaration, ContentHashList hashes, CancellationToken token)
    {
        if (hashes.Hashes.Count != declaration.Outputs.Length + 1 || hashes.Payload is not null)
        {
            throw new InvalidDataException(SR.InvalidManifest);
        }

        OpenStreamResult opened = session.OpenStreamAsync(s_context, hashes.Hashes[0], token).GetAwaiter().GetResult();
        using Stream? manifest = opened.Stream;
        Check(opened, token);
        if (manifest is null)
        {
            throw new InvalidDataException(SR.InvalidManifest);
        }

        if (new ContentHash(HashType.SHA256, Hash(manifest, token)) != hashes.Hashes[0])
        {
            throw new InvalidDataException(SR.InvalidManifest);
        }

        manifest.Position = 0;
        using var reader = new BinaryReader(manifest);
        Require(reader.ReadInt32() == FormatVersion && reader.ReadInt32() == declaration.Outputs.Length);
        var metadata = new (bool ReadOnly, int Mode)[declaration.Outputs.Length];
        for (int i = 0; i < declaration.Outputs.Length; i++)
        {
            Require(TaskValueCodec.ReadString(reader) == declaration.Outputs[i]);
            byte[] hash = reader.ReadBytes(HashSize);
            Require(hash.Length == HashSize && new ContentHash(HashType.SHA256, hash) == hashes.Hashes[i + 1]);
            bool readOnly = TaskValueCodec.ReadBoolean(reader);
            int mode = reader.ReadInt32();
            Require(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? mode == -1 : mode >= 0 && mode <= 0xfff);
            metadata[i] = (readOnly, mode);
        }

        Require(reader.ReadInt32() == declaration.OutputValues.Length);
        var values = new Action[declaration.OutputValues.Length];
        for (int i = 0; i < values.Length; i++)
        {
            TaskDeclaration.OutputValue output = declaration.OutputValues[i];
            Require(TaskValueCodec.ReadString(reader) == output.Name);
            int length = TaskValueCodec.ReadCount(reader);
            byte[] bytes = reader.ReadBytes(length);
            Require(bytes.Length == length);
            values[i] = output.Prepare(bytes);
        }

        Require(manifest.Position == manifest.Length);
        for (int i = 0; i < declaration.Outputs.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            string path = declaration.Outputs[i];
            RejectLinkedAncestors(Path.GetDirectoryName(path));
            if (File.Exists(path))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) == 0 && (attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }
            }

            Check(session.PlaceFileAsync(s_context, hashes.Hashes[i + 1], new AbsolutePath(path), FileAccessMode.Write, FileReplacementMode.ReplaceExisting, FileRealizationMode.Copy, token).GetAwaiter().GetResult(), token);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (metadata[i].ReadOnly)
                {
                    File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                }
            }
#if !NETFRAMEWORK
            else
            {
                File.SetUnixFileMode(path, (UnixFileMode)metadata[i].Mode);
            }
#endif
        }

        for (int i = 0; i < values.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            values[i]();
        }

        token.ThrowIfCancellationRequested();
    }

    private static (bool ReadOnly, int Mode) ReadMetadata(string path)
    {
        RejectLinkedAncestors(path);
        if (!File.Exists(path) || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new IOException(SR.Format(SR.InvalidOutputFile, path));
        }

        int mode = -1;
#if !NETFRAMEWORK
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            mode = (int)File.GetUnixFileMode(path);
        }
#endif
        return ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0, mode);
    }

    private static void RejectLinkedAncestors(string? path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(SR.Format(SR.InvalidOutputFile, current));
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidDataException(SR.InvalidManifest);
        }
    }

    private static void Check(ResultBase result, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!result.Succeeded)
        {
            throw new CacheException(SR.Format(SR.CacheFailure, result.ToString()));
        }
    }

    private sealed class CacheException(string message) : Exception(message);

    private sealed class CacheLifetime : IDisposable
    {
        internal ICache Cache { get; }
        internal CacheLifetime(string directory)
        {
            Cache = CacheStore.Create(directory);
            bool started = false;
            try
            {
                Check(Cache.StartupAsync(s_context).GetAwaiter().GetResult());
                started = true;
            }
            finally
            {
                if (!started)
                {
                    Cache.Dispose();
                }
            }
        }

        public void Dispose()
        {
            try { Check(Cache.ShutdownAsync(s_context).GetAwaiter().GetResult()); }
            finally { Cache.Dispose(); }
        }
    }

    private sealed class SessionLifetime : IDisposable
    {
        internal ICacheSession Session { get; }
        internal SessionLifetime(ICache cache)
        {
            CreateSessionResult<ICacheSession> created = cache.CreateSession(s_context, nameof(DeclaredIOTask), ImplicitPin.PutAndGet);
            Check(created);
            Session = created.Session ?? throw new InvalidDataException(SR.InvalidManifest);
            bool started = false;
            try
            {
                Check(Session.StartupAsync(s_context).GetAwaiter().GetResult());
                started = true;
            }
            finally
            {
                if (!started)
                {
                    Session.Dispose();
                }
            }
        }

        public void Dispose()
        {
            try { Check(Session.ShutdownAsync(s_context).GetAwaiter().GetResult()); }
            finally { Session.Dispose(); }
        }
    }
}
