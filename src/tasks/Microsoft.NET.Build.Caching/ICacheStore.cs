// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.NET.Build.Caching;

internal interface ICacheStore
{
    ValueTask<ICacheSession> OpenSessionAsync(CancellationToken cancellationToken);
}

// Disposal waits for operations. Returned streams own independent maintenance leases.
internal interface ICacheSession : IContentSession, IMemoizationSession, IAsyncDisposable
{
}

internal interface IContentSession
{
    Task<ContentHash> PutFileAsync(string sourcePath, CancellationToken cancellationToken);
    Task<ContentHash> PutStreamAsync(Stream source, CancellationToken cancellationToken);
    Task<Stream> OpenStreamAsync(ContentHash hash, CancellationToken cancellationToken);
    Task PlaceFileAsync(ContentHash hash, string destinationPath, CancellationToken cancellationToken);
}

internal interface IMemoizationSession
{
    // Only an absent entry is a miss. Missing referenced content is an error.
    Task<CacheEntry?> GetAsync(Fingerprint key, CancellationToken cancellationToken);
    Task<PublishResult> AddOrGetAsync(Fingerprint key, CacheEntry entry, CancellationToken cancellationToken);
}

internal enum PublishResult
{
    Added,
    Equivalent
}
