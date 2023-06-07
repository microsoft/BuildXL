// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Sharding scheme that doesn't actually shard, but instead always returns the same shard id.
/// </summary>
/// <remarks>
/// Used for single servers
/// </remarks>
public record SingleShardingScheme<TKey, TShardId> : IShardingScheme<TKey, TShardId>
{
    public ShardingAlgorithm Algorithm => ShardingAlgorithm.SingleShard;

    public IReadOnlyList<TShardId> Locations { get; }

    public SingleShardingScheme(TShardId location)
    {
        Locations = new List<TShardId>() { location };
    }

    public TShardId Locate(TKey key)
    {
        return Locations[0];
    }
}
