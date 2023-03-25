// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// This is basically a function that maps a key to a location (i.e., a map).
/// </summary>
public interface IShardingScheme<in TKey, out TLocation>
{
    public IReadOnlyList<TLocation> Locations { get; }

    public TLocation Locate(TKey key);
}
