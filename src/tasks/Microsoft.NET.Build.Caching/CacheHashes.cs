// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;

namespace Microsoft.NET.Build.Caching;

/// <summary>Identifies stored bytes by their SHA-256 digest.</summary>
internal readonly struct ContentHash : IEquatable<ContentHash>
{
    private readonly Sha256Digest _digest;

    internal ContentHash(ReadOnlySpan<byte> bytes) => _digest = new Sha256Digest(bytes);
    internal byte[] ToByteArray() => _digest.ToByteArray();
    public override string ToString() => _digest.ToString();
    public bool Equals(ContentHash other) => _digest.Equals(other._digest);
    public override bool Equals(object? obj) => obj is ContentHash other && Equals(other);
    public override int GetHashCode() => _digest.GetHashCode();
    public static bool operator ==(ContentHash left, ContentHash right) => left.Equals(right);
    public static bool operator !=(ContentHash left, ContentHash right) => !left.Equals(right);
}

/// <summary>Identifies an operation by a caller-supplied SHA-256 digest.</summary>
internal readonly struct CacheKey : IEquatable<CacheKey>
{
    private readonly Sha256Digest _digest;

    internal CacheKey(ReadOnlySpan<byte> bytes) => _digest = new Sha256Digest(bytes);
    internal byte[] ToByteArray() => _digest.ToByteArray();
    public override string ToString() => _digest.ToString();
    public bool Equals(CacheKey other) => _digest.Equals(other._digest);
    public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);
    public override int GetHashCode() => _digest.GetHashCode();
    public static bool operator ==(CacheKey left, CacheKey right) => left.Equals(right);
    public static bool operator !=(CacheKey left, CacheKey right) => !left.Equals(right);
}

internal readonly struct Sha256Digest : IEquatable<Sha256Digest>
{
    internal const int Length = 32;

    private readonly ulong _first;
    private readonly ulong _second;
    private readonly ulong _third;
    private readonly ulong _fourth;

    internal Sha256Digest(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
        {
            throw new ArgumentException(SR.HashLength, nameof(bytes));
        }

        _first = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        _second = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8));
        _third = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(16));
        _fourth = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(24));
    }

    internal byte[] ToByteArray()
    {
        var bytes = new byte[Length];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, _first);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), _second);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), _third);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), _fourth);
        return bytes;
    }

    public override string ToString()
    {
        const string Hex = "0123456789abcdef";
        byte[] bytes = ToByteArray();
        var characters = new char[Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            characters[i * 2] = Hex[bytes[i] >> 4];
            characters[i * 2 + 1] = Hex[bytes[i] & 15];
        }

        return new string(characters);
    }

    public bool Equals(Sha256Digest other) =>
        _first == other._first && _second == other._second && _third == other._third && _fourth == other._fourth;

    public override bool Equals(object? obj) => obj is Sha256Digest other && Equals(other);

    public override int GetHashCode() => unchecked(
        ((_first.GetHashCode() * 31 + _second.GetHashCode()) * 31 + _third.GetHashCode()) * 31 + _fourth.GetHashCode());
}
