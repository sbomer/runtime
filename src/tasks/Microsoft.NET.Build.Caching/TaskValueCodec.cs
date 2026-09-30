// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.NET.Build.Caching;

internal sealed class TaskValueCodec
{
    private enum Kind : byte
    {
        String,
        Boolean,
        Char,
        Byte,
        SByte,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Single,
        Double,
        Decimal,
        DateTime,
        Item
    }

    private static readonly Type[] s_types =
    {
        typeof(string), typeof(bool), typeof(char), typeof(byte), typeof(sbyte),
        typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long),
        typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(DateTime), typeof(ITaskItem)
    };

    private readonly Type _elementType;
    private readonly Kind _kind;
    private readonly bool _array;

    internal TaskValueCodec(Type type)
    {
        _array = type.IsArray;
        _elementType = _array ? type.GetElementType()! : type;
        int index = Array.IndexOf(s_types, _elementType);
        if (index < 0 || (_array && type != _elementType.MakeArrayType()))
        {
            throw new ArgumentException(SR.Format(SR.UnsupportedValueType, type));
        }

        _kind = (Kind)index;
    }

    internal void WriteSchema(BinaryWriter writer)
    {
        writer.Write((byte)_kind);
        writer.Write(_array);
    }

    internal byte[] Serialize(object? value)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteSchema(writer);
        writer.Write(value is not null);
        if (value is not null)
        {
            if (_array)
            {
                var array = (Array)value;
                writer.Write(array.Length);
                foreach (object? element in array)
                {
                    WriteScalar(writer, element);
                }
            }
            else
            {
                WriteScalar(writer, value);
            }
        }

        return stream.ToArray();
    }

    internal object? Deserialize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadByte() != (byte)_kind || ReadBoolean(reader) != _array)
        {
            throw new InvalidDataException(SR.InvalidManifest);
        }

        object? value = null;
        if (ReadBoolean(reader))
        {
            if (_array)
            {
                int count = ReadCount(reader);
                Array array = Array.CreateInstance(_elementType, count);
                for (int i = 0; i < count; i++)
                {
                    array.SetValue(ReadScalar(reader), i);
                }

                value = array;
            }
            else
            {
                value = ReadScalar(reader);
            }
        }
        else if (!_array && _elementType.IsValueType)
        {
            throw new InvalidDataException(SR.InvalidManifest);
        }

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException(SR.InvalidManifest);
        }

        return value;
    }

    private void WriteScalar(BinaryWriter writer, object? value)
    {
        if (_kind is Kind.String or Kind.Item)
        {
            writer.Write(value is not null);
            if (value is null)
            {
                return;
            }
        }

        Debug.Assert(value is not null);
        switch (_kind)
        {
            case Kind.String: WriteString(writer, (string)value!); break;
            case Kind.Boolean: writer.Write((bool)value!); break;
            case Kind.Char: writer.Write((ushort)(char)value!); break;
            case Kind.Byte: writer.Write((byte)value!); break;
            case Kind.SByte: writer.Write((sbyte)value!); break;
            case Kind.Int16: writer.Write((short)value!); break;
            case Kind.UInt16: writer.Write((ushort)value!); break;
            case Kind.Int32: writer.Write((int)value!); break;
            case Kind.UInt32: writer.Write((uint)value!); break;
            case Kind.Int64: writer.Write((long)value!); break;
            case Kind.UInt64: writer.Write((ulong)value!); break;
            case Kind.Single: writer.Write((float)value!); break;
            case Kind.Double: writer.Write((double)value!); break;
            case Kind.Decimal: writer.Write((decimal)value!); break;
            case Kind.DateTime: writer.Write(((DateTime)value!).ToBinary()); break;
            case Kind.Item:
                var item = (ITaskItem)value!;
                ITaskItem2? escapedItem = item as ITaskItem2;
                WriteString(writer, escapedItem is null ? ProjectCollection.Escape(item.ItemSpec) : escapedItem.EvaluatedIncludeEscaped);
                WriteString(writer, escapedItem is null ? ProjectCollection.Escape(item.GetMetadata("DefiningProjectFullPath")) : escapedItem.GetMetadataValueEscaped("DefiningProjectFullPath"));
                IDictionary metadata = escapedItem is null ? item.CloneCustomMetadata() : escapedItem.CloneCustomMetadataEscaped();
                string[] names = metadata.Keys.Cast<string>().OrderBy(name => name, StringComparer.Ordinal).ToArray();
                writer.Write(names.Length);
                foreach (string name in names)
                {
                    WriteString(writer, name);
                    string metadataValue = (string)metadata[name]!;
                    WriteString(writer, escapedItem is null ? ProjectCollection.Escape(metadataValue) : metadataValue);
                }

                break;
        }
    }

    private object? ReadScalar(BinaryReader reader)
    {
        if (_kind is Kind.String or Kind.Item && !ReadBoolean(reader))
        {
            return null;
        }

        return _kind switch
        {
            Kind.String => ReadString(reader),
            Kind.Boolean => ReadBoolean(reader),
            Kind.Char => (char)reader.ReadUInt16(),
            Kind.Byte => reader.ReadByte(),
            Kind.SByte => reader.ReadSByte(),
            Kind.Int16 => reader.ReadInt16(),
            Kind.UInt16 => reader.ReadUInt16(),
            Kind.Int32 => reader.ReadInt32(),
            Kind.UInt32 => reader.ReadUInt32(),
            Kind.Int64 => reader.ReadInt64(),
            Kind.UInt64 => reader.ReadUInt64(),
            Kind.Single => reader.ReadSingle(),
            Kind.Double => reader.ReadDouble(),
            Kind.Decimal => reader.ReadDecimal(),
            Kind.DateTime => DateTime.FromBinary(reader.ReadInt64()),
            Kind.Item => ReadItem(reader),
            _ => throw new InvalidDataException(SR.InvalidManifest)
        };
    }

    private static TaskItem ReadItem(BinaryReader reader)
    {
        var item = new TaskItem();
        string itemSpec = ReadString(reader);
        string definingProject = ReadString(reader);
        int count = ReadCount(reader, minimumBytes: 8);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            string name = ReadString(reader);
            if (!names.Add(name))
            {
                throw new InvalidDataException(SR.InvalidManifest);
            }

            item.SetMetadata(name, ReadString(reader));
        }

        return new TaskItem(new ItemSnapshot(item, itemSpec, definingProject));
    }

    internal static void WriteString(BinaryWriter writer, string value)
    {
        // Encode UTF-16 code units, including unpaired surrogates, without lossy transcoding.
        writer.Write(value.Length);
        foreach (char character in value)
        {
            writer.Write((ushort)character);
        }
    }

    internal static string ReadString(BinaryReader reader)
    {
        int count = ReadCount(reader, minimumBytes: 2);
        var characters = new char[count];
        for (int i = 0; i < count; i++)
        {
            characters[i] = (char)reader.ReadUInt16();
        }

        return new string(characters);
    }

    internal static int ReadCount(BinaryReader reader, int minimumBytes = 1)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > (reader.BaseStream.Length - reader.BaseStream.Position) / minimumBytes)
        {
            throw new InvalidDataException(SR.InvalidManifest);
        }

        return count;
    }

    internal static bool ReadBoolean(BinaryReader reader) => reader.ReadByte() switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException(SR.InvalidManifest)
    };

    private sealed class ItemSnapshot(TaskItem item, string itemSpec, string definingProject) : ITaskItem2
    {
        // TaskItem's copy constructor preserves this verbatim; its string constructor normalizes paths.
        public string EvaluatedIncludeEscaped { get; set; } = itemSpec;
        public string ItemSpec { get => ProjectCollection.Unescape(EvaluatedIncludeEscaped); set => EvaluatedIncludeEscaped = value; }
        public int MetadataCount => item.MetadataCount;
        public ICollection MetadataNames => item.MetadataNames;
        public string GetMetadata(string name) => ProjectCollection.Unescape(GetMetadataValueEscaped(name));
        public string GetMetadataValueEscaped(string name) => name.Equals("DefiningProjectFullPath", StringComparison.OrdinalIgnoreCase) ? definingProject : ((ITaskItem2)item).GetMetadataValueEscaped(name);
        public void SetMetadata(string name, string value) => item.SetMetadata(name, value);
        public void SetMetadataValueLiteral(string name, string value) => ((ITaskItem2)item).SetMetadataValueLiteral(name, value);
        public void RemoveMetadata(string name) => item.RemoveMetadata(name);
        public IDictionary CloneCustomMetadata() => item.CloneCustomMetadata();
        public IDictionary CloneCustomMetadataEscaped() => ((ITaskItem2)item).CloneCustomMetadataEscaped();
        public void CopyMetadataTo(ITaskItem destinationItem)
        {
            // TaskItem.CopyMetadataTo would add OriginalItemSpec, changing the captured output.
            foreach (DictionaryEntry entry in CloneCustomMetadataEscaped())
            {
                destinationItem.SetMetadata((string)entry.Key, (string)entry.Value!);
            }
        }
    }
}
