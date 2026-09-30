// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Microsoft.NET.Build.Caching;

internal sealed class ContentHash : IEquatable<ContentHash>
{
    internal const int ByteLength = 32;

    internal ContentHash(string hex)
    {
        if (hex.Length != ByteLength * 2 || hex.Any(c => !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))))
        {
            throw new ArgumentException(SR.InvalidCacheHash, nameof(hex));
        }

        Hex = hex;
    }

    internal string Hex { get; }

    internal static ContentHash FromBytes(byte[] bytes) =>
#if NETFRAMEWORK
        new ContentHash(BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant());
#else
        new ContentHash(Convert.ToHexStringLower(bytes));
#endif

    internal byte[] ToBytes()
    {
        byte[] bytes = new byte[ByteLength];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(Hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    public bool Equals(ContentHash? other) => other is not null && Hex == other.Hex;
    public override bool Equals(object? obj) => obj is ContentHash other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Hex);
    public override string ToString() => Hex;
}

internal sealed class Fingerprint
{
    internal Fingerprint(string hex) => Hex = new ContentHash(hex).Hex;
    internal string Hex { get; }
    public override string ToString() => Hex;
}

internal sealed class CacheEntry : IEquatable<CacheEntry>
{
    private const int FormatVersion = 2;
    private const int MaximumPayloadLength = 1024;

    internal CacheEntry(IEnumerable<ContentHash> contentHashes, byte[]? payload = null)
    {
        if (payload?.Length > MaximumPayloadLength)
        {
            throw new ArgumentException(SR.Format(SR.CachePayloadTooLarge, MaximumPayloadLength), nameof(payload));
        }

        ContentHashes = Array.AsReadOnly(contentHashes.ToArray());
        Payload = payload is null ? null : Array.AsReadOnly(payload.ToArray());
    }

    internal ReadOnlyCollection<ContentHash> ContentHashes { get; }
    internal ReadOnlyCollection<byte>? Payload { get; }

    public bool Equals(CacheEntry? other) =>
        other is not null && ContentHashes.SequenceEqual(other.ContentHashes) &&
        (Payload is null ? other.Payload is null : other.Payload is not null && Payload.SequenceEqual(other.Payload));

    public override bool Equals(object? obj) => obj is CacheEntry other && Equals(other);
    public override int GetHashCode()
    {
        int hashCode = Payload is null ? 0 : 1;
        foreach (ContentHash hash in ContentHashes)
        {
            hashCode = unchecked(hashCode * 31 + hash.GetHashCode());
        }

        if (Payload is not null)
        {
            foreach (byte value in Payload)
            {
                hashCode = unchecked(hashCode * 31 + value);
            }
        }

        return hashCode;
    }

    internal void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(FormatVersion);
        writer.Write(ContentHashes.Count);
        foreach (ContentHash hash in ContentHashes)
        {
            writer.Write(hash.ToBytes());
        }

        writer.Write(Payload?.Count ?? -1);
        if (Payload is not null)
        {
            foreach (byte value in Payload)
            {
                writer.Write(value);
            }
        }
    }

    internal static CacheEntry Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (stream.Length < sizeof(int) * 3 || reader.ReadInt32() != FormatVersion)
        {
            throw new InvalidDataException(SR.InvalidCacheEntry);
        }

        int count = reader.ReadInt32();
        if (count < 0 || stream.Length - stream.Position < (long)count * ContentHash.ByteLength + sizeof(int))
        {
            throw new InvalidDataException(SR.InvalidCacheEntry);
        }

        var hashes = new ContentHash[count];
        for (int i = 0; i < count; i++)
        {
            hashes[i] = ContentHash.FromBytes(reader.ReadBytes(ContentHash.ByteLength));
        }

        int payloadLength = reader.ReadInt32();
        if (payloadLength < -1 || payloadLength > MaximumPayloadLength ||
            stream.Length - stream.Position != Math.Max(0, payloadLength))
        {
            throw new InvalidDataException(SR.InvalidCacheEntry);
        }

        return new CacheEntry(hashes, payloadLength == -1 ? null : reader.ReadBytes(payloadLength));
    }
}
