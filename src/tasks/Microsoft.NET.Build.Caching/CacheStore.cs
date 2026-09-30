// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Sessions;

namespace Microsoft.NET.Build.Caching;

internal static class CacheStore
{
    internal static ICache Create(string directory) => new ConcurrentLocalCache(new AbsolutePath(directory));
}
