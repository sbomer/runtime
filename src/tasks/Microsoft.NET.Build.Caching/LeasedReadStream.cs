// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.NET.Build.Caching;

internal sealed class LeasedReadStream : Stream
{
    private readonly Stream _stream;
    private IDisposable? _lease;

    internal LeasedReadStream(Stream stream, IDisposable lease)
    {
        _stream = stream;
        _lease = lease;
    }

    public override bool CanRead => _stream.CanRead;
    public override bool CanSeek => _stream.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _stream.Length;
    public override long Position { get => _stream.Position; set => _stream.Position = value; }
    public override void Flush() => _stream.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _stream.ReadAsync(buffer, offset, count, cancellationToken);
#if !NETFRAMEWORK
    public override int Read(Span<byte> buffer) => _stream.Read(buffer);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _stream.ReadAsync(buffer, cancellationToken);
#endif
    public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException(SR.CacheStreamReadOnly);
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException(SR.CacheStreamReadOnly);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                _stream.Dispose();
            }
            finally
            {
                Interlocked.Exchange(ref _lease, null)?.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
