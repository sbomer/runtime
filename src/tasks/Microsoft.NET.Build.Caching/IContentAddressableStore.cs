// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.NET.Build.Caching;

/// <summary>Stores immutable content identified by its SHA-256 digest.</summary>
internal interface IContentAddressableStore
{
    /// <summary>Copies and hashes content from the current position to EOF without disposing the source.</summary>
    /// <remarks>Existing content is verified and reused. Cancellation does not undo completed publication.</remarks>
    ValueTask<ContentHash> PutAsync(Stream source, CancellationToken cancellationToken = default);

    /// <summary>Verifies content and returns a read-only stream positioned at its beginning.</summary>
    /// <remarks>The caller owns the returned stream. Missing or corrupt content is an error.</remarks>
    ValueTask<Stream> GetAsync(ContentHash hash, CancellationToken cancellationToken = default);
}
