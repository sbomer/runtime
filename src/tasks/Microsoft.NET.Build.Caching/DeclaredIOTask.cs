// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Build.Framework;

namespace Microsoft.NET.Build.Caching;

/// <summary>Executes a deterministic operation with explicitly declared inputs and outputs and optional caching.</summary>
/// <remarks>This prototype requires task authors to declare every dependency and replayable output property.</remarks>
public abstract class DeclaredIOTask : Microsoft.Build.Utilities.Task, ICancelableTask
{
    private const int FormatVersion = 1;
    private const int HashSize = 32;
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
    /// <remarks>Each execution captures this setting before calling <see cref="DescribeOperation"/>.</remarks>
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
                Log.LogError(SR.TaskCacheDirectoryRequired);
                return false;
            }

            string workingDirectory = Environment.CurrentDirectory;
            string cacheDirectory = Path.GetFullPath(Path.Combine(workingDirectory, CacheDirectory));
            bool cacheEnabled = CacheEnabled;
            var declaration = new TaskDeclaration(cacheEnabled);
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
                    throw new FileNotFoundException(SR.Format(SR.MissingInputFile, input), input);
                }
            }

            if (!cacheEnabled)
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

            CacheKey key = Fingerprint(declaration, workingDirectory, token);
            var cache = new DiskCache(cacheDirectory);
            ContentHash? manifest = cache.Index.GetAsync(key, token).AsTask().GetAwaiter().GetResult();
            if (manifest.HasValue)
            {
                Restore(cache.Cas, declaration, manifest.Value, token);
                Log.LogMessage(MessageImportance.Low, SR.CacheHit);
                return !engine.HasErrors;
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

            if (!Publish(cache, key, declaration, engine, token) && !engine.HasErrors)
            {
                Log.LogMessage(MessageImportance.Low, SR.CacheWarningBypass);
            }

            token.ThrowIfCancellationRequested();
            return !engine.HasErrors;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Log.LogMessage(MessageImportance.Low, SR.TaskCancelled);
            return false;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
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

    private CacheKey Fingerprint(TaskDeclaration declaration, string workingDirectory, CancellationToken token)
    {
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data);
        writer.Write(FormatVersion);
        Type taskType = GetType();
        TaskValueCodec.WriteString(writer, taskType.FullName ?? throw new NotSupportedException(SR.Format(SR.TaskTypeNameRequired, taskType)));
        for (Type? type = taskType; type is not null; type = type.BaseType)
        {
            writer.Write(type.Module.ModuleVersionId.ToByteArray());
            bool hasTaskBase = type != typeof(DeclaredIOTask);
            writer.Write(hasTaskBase);
            if (!hasTaskBase)
            {
                break;
            }
        }

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
        return new CacheKey(Hash(data, token));
    }

    private static byte[] Hash(Stream stream, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using SHA256 hash = SHA256.Create();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
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
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static bool Publish(DiskCache cache, CacheKey key, TaskDeclaration declaration, TrackingBuildEngine engine, CancellationToken token)
    {
        var values = new byte[declaration.OutputValues.Length][];
        for (int i = 0; i < values.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            values[i] = declaration.OutputValues[i].Capture();
        }

        token.ThrowIfCancellationRequested();
        if (engine.HasErrors || engine.HasWarnings)
        {
            return false;
        }

        using var manifest = new MemoryStream();
        using var writer = new BinaryWriter(manifest, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatVersion);
        writer.Write(declaration.Outputs.Length);
        for (int i = 0; i < declaration.Outputs.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            string path = declaration.Outputs[i];
            (bool readOnly, int mode) = ReadMetadata(path);
            using var source = File.OpenRead(path);
#pragma warning disable CA2025 // GetResult completes the asynchronous put before disposing the source.
            ContentHash hash = cache.Cas.PutAsync(source, token).AsTask().GetAwaiter().GetResult();
#pragma warning restore CA2025
            TaskValueCodec.WriteString(writer, path);
            writer.Write(hash.ToByteArray());
            writer.Write(readOnly);
            writer.Write(mode);
        }

        writer.Write(declaration.OutputValues.Length);
        for (int i = 0; i < values.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            TaskValueCodec.WriteString(writer, declaration.OutputValues[i].Name);
            writer.Write(values[i].Length);
            writer.Write(values[i]);
        }

        manifest.Position = 0;
#pragma warning disable CA2025 // GetResult completes the asynchronous put before disposing the manifest.
        ContentHash manifestHash = cache.Cas.PutAsync(manifest, token).AsTask().GetAwaiter().GetResult();
#pragma warning restore CA2025
        ContentHash winner = cache.Index.GetOrAddAsync(key, manifestHash, token).AsTask().GetAwaiter().GetResult();
        if (winner != manifestHash)
        {
            throw new InvalidDataException(SR.CacheConflict);
        }

        return true;
    }

    private static void Restore(IContentAddressableStore cas, TaskDeclaration declaration, ContentHash manifestHash, CancellationToken token)
    {
        using Stream manifest = cas.GetAsync(manifestHash, token).AsTask().GetAwaiter().GetResult();
        using var reader = new BinaryReader(manifest);
        Require(reader.ReadInt32() == FormatVersion && reader.ReadInt32() == declaration.Outputs.Length);
        var hashes = new ContentHash[declaration.Outputs.Length];
        var metadata = new (bool ReadOnly, int Mode)[declaration.Outputs.Length];
        for (int i = 0; i < declaration.Outputs.Length; i++)
        {
            Require(TaskValueCodec.ReadString(reader) == declaration.Outputs[i]);
            byte[] hash = reader.ReadBytes(HashSize);
            Require(hash.Length == HashSize);
            hashes[i] = new ContentHash(hash);
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
            using Stream source = cas.GetAsync(hashes[i], token).AsTask().GetAwaiter().GetResult();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(path))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) == 0 && (attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }
            }

            File.Delete(path);
            using (FileStream destination = DiskCacheFileSystem.CreatePrivateFile(path, FileAccess.Write))
            {
#pragma warning disable CA2025 // GetResult completes the copy before disposing either stream.
                source.CopyToAsync(destination, 65536, token).GetAwaiter().GetResult();
#pragma warning restore CA2025
            }

            token.ThrowIfCancellationRequested();
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
}
