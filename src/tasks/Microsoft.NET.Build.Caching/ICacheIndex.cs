// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.NET.Build.Caching;

/// <summary>Maps operation keys to content hashes without replacing existing mappings.</summary>
internal interface ICacheIndex
{
    /// <summary>Returns the stored hash, or null if the key is absent.</summary>
    /// <remarks>Storage failures and malformed records are errors, not misses.</remarks>
    ValueTask<ContentHash?> GetAsync(CacheKey key, CancellationToken cancellationToken = default);

    /// <summary>Atomically inserts an absent key and returns the winning value, whether new or existing.</summary>
    /// <remarks>
    /// The caller compares the returned value with its candidate to detect conflicts.
    /// The index does not inspect content or require it to exist. Cancellation does not undo completed publication.
    /// </remarks>
    ValueTask<ContentHash> GetOrAddAsync(CacheKey key, ContentHash value, CancellationToken cancellationToken = default);
}
