// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob;

/// <summary>
/// Implements the jump consistent hashing scheme described in the paper "A Fast, Minimal Memory, Consistent Hash
/// Algorithm" by John Lamping and Eric Veach.
/// 
/// See: https://arxiv.org/pdf/1406.2294
/// </summary>
/// <remarks>
/// This consistent hashing scheme is good for situations where servers don't go away ever, although we can add or remove shards.
/// </remarks>
public record JumpConsistentHash<TShardId> : IShardingScheme<int, TShardId>
{
    public IReadOnlyList<TShardId> Locations { get; }

    public JumpConsistentHash(IReadOnlyList<TShardId> locations)
    {
        if (locations.Count == 0)
        {
            throw new ArgumentException(message: "There must be at least 1 shard", paramName: nameof(locations));
        }

        Locations = locations;
    }

    public TShardId Locate(int key)
    {
        // TODO: this could be made faster with a faster PRNG, or just avoiding the float arithmetic altogether.
        var random = new Random(key);
        int beforePreviousJump = 1;
        int beforeCurrentJump = 0;
        while (beforeCurrentJump < Locations.Count)
        {
            beforePreviousJump = beforeCurrentJump;
            var r = random.NextDouble();
            beforeCurrentJump = (int)Math.Floor((beforePreviousJump + 1) / r);
        }

        return Locations[beforePreviousJump];
    }
}
