// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    private readonly TShardId _location;

    public SingleShardingScheme(TShardId location)
    {
        _location = location;
    }

    public Shard<TShardId> Locate(TKey key)
    {
        return _location;
    }
}
