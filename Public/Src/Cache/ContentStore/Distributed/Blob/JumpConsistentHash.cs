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
/// This consistent hashing scheme is good for situations where servers are never unavailable, although we can add or
/// remove shards.
/// </remarks>
public record JumpConsistentHash<TShardId> : IShardingScheme<int, TShardId>
{
    private readonly IReadOnlyList<TShardId> _locations;

    public JumpConsistentHash(IReadOnlyList<TShardId> locations)
    {
        if (locations.Count == 0)
        {
            throw new ArgumentException(message: "There must be at least 1 shard", paramName: nameof(locations));
        }

        _locations = locations;
    }

    public Shard<TShardId> Locate(int key)
    {
        // TODO: this could be made faster with a faster PRNG, or just avoiding the float arithmetic altogether.
        var random = new Random(key);
        int beforePreviousJump = 1;
        int beforeCurrentJump = 0;
        while (beforeCurrentJump < _locations.Count)
        {
            beforePreviousJump = beforeCurrentJump;
            var r = random.NextDouble();

            try
            {
                beforeCurrentJump = (int)Math.Floor((beforePreviousJump + 1) / r);
            }
            catch (OverflowException)
            {
                // r can sometimes be extremely small (for example, 1e-10) and cause overflow to happen. In such cases,
                // beforeCurrentJump would be set to an extremely large number anyways, and so the loop would break and
                // return the same thing as if we break now.
                break;
            }
        }

        return _locations[beforePreviousJump];
    }
}
