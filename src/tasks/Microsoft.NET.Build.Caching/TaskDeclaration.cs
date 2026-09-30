// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;

namespace Microsoft.NET.Build.Caching;

/// <summary>Describes the files and values consumed and produced by a deterministic task.</summary>
public sealed class TaskDeclaration
{
    private readonly List<string> _inputs = new();
    private readonly List<string> _outputs = new();
    private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OutputValue> _outputValues = new(StringComparer.Ordinal);
    private bool _sealed;

    internal TaskDeclaration() { }

    internal string[] Inputs { get; private set; } = Array.Empty<string>();
    internal string[] Outputs { get; private set; } = Array.Empty<string>();
    internal KeyValuePair<string, byte[]>[] Values { get; private set; } = Array.Empty<KeyValuePair<string, byte[]>>();
    internal OutputValue[] OutputValues { get; private set; } = Array.Empty<OutputValue>();

    /// <summary>Declares an input file whose path and contents affect execution.</summary>
    /// <param name="path">The input path, relative to the task's working directory or absolute.</param>
    public void AddInputFile(string path)
    {
        CheckName(path);
        _inputs.Add(path);
    }

    /// <summary>Declares a regular output file that successful execution must produce.</summary>
    /// <param name="path">The output path, relative to the task's working directory or absolute.</param>
    public void AddOutputFile(string path)
    {
        CheckName(path);
        _outputs.Add(path);
    }

    /// <summary>Snapshots a named execution-affecting value.</summary>
    /// <typeparam name="T">A supported MSBuild scalar type, task item, or one-dimensional array.</typeparam>
    /// <param name="name">The unique value name.</param>
    /// <param name="value">The value to include in the fingerprint.</param>
    public void AddValue<T>(string name, T? value)
    {
        CheckName(name);
        _values.Add(name, new TaskValueCodec(typeof(T)).Serialize(value));
    }

    /// <summary>Registers typed accessors for capturing and replaying an output property.</summary>
    /// <typeparam name="T">A supported MSBuild scalar type, task item, or one-dimensional array.</typeparam>
    /// <param name="name">The unique output property name.</param>
    /// <param name="get">The accessor used after successful execution.</param>
    /// <param name="set">The accessor used to restore a cached result.</param>
    public void AddOutputValue<T>(string name, Func<T?> get, Action<T?> set)
    {
        CheckName(name);
        var codec = new TaskValueCodec(typeof(T));
        _outputValues.Add(name, new OutputValue(name, codec, () => codec.Serialize(get()), bytes =>
        {
            T? value = (T?)codec.Deserialize(bytes);
            return () => set(value);
        }));
    }

    /// <summary>Registers accessors for an output item, including its custom metadata.</summary>
    /// <param name="name">The unique output property name.</param>
    /// <param name="get">The accessor used after successful execution.</param>
    /// <param name="set">The accessor used to restore a cached result.</param>
    public void AddOutputItem(string name, Func<ITaskItem?> get, Action<ITaskItem?> set) => AddOutputValue(name, get, set);

    private void CheckName(string name)
    {
        if (_sealed)
        {
            throw new InvalidOperationException(SR.DeclarationSealed);
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException(SR.EmptyDeclarationName, nameof(name));
        }
    }

    internal void Seal(string workingDirectory)
    {
        _sealed = true;
        StringComparer comparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Inputs = Normalize(_inputs);
        Outputs = Normalize(_outputs);
        if (Inputs.Intersect(Outputs, comparer).Any())
        {
            throw new ArgumentException(SR.OverlappingPaths);
        }

        Values = _values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        OutputValues = _outputValues.Values.OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();

        string[] Normalize(List<string> paths)
        {
            string[] normalized = paths.Select(path => Path.GetFullPath(Path.Combine(workingDirectory, path))).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (normalized.Distinct(comparer).Count() != normalized.Length)
            {
                throw new ArgumentException(SR.DuplicatePaths);
            }

            return normalized;
        }
    }

    internal sealed class OutputValue(string name, TaskValueCodec codec, Func<byte[]> capture, Func<byte[], Action> prepare)
    {
        internal string Name { get; } = name;
        internal TaskValueCodec Codec { get; } = codec;
        internal Func<byte[]> Capture { get; } = capture;
        internal Func<byte[], Action> Prepare { get; } = prepare;
    }
}
