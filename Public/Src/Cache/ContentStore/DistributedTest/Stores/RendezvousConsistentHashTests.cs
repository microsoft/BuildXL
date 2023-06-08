// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Data;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Utilities.Core;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Stores;

public class RendezvousConsistentHashTests
{
    [Theory]
    [InlineData(5, 10000, 0.05)]
    [InlineData(10, 50000, 0.05)]
    [InlineData(100, 100000, 0.05)]
    public void LocateReturnsShardsWithRoughlyUniformDistribution(int nodes, int sampleSize, double margin)
    {
        var shardManager = new ShardManager(Enumerable.Range(0, nodes));
        Func<int, int> hasher = x => HashCodeHelper.GetHashCode(x);
        var hash = new RendezvousConsistentHash<int>(shardManager, hasher);

        var results = Enumerable.Range(0, sampleSize)
            .Select(x => hash.Locate(x)!.Location)
            .GroupBy(x => x)
            .Select(x => x.Count())
            .ToArray();

        foreach (var count in results)
        {
            var errorMargin = Math.Abs(((double)count / sampleSize) - (1.0 / nodes));
            Assert.True(errorMargin < margin, $"Distribution is not uniform, error margin: {errorMargin}");
        }
    }

    [Theory]
    [InlineData(5, 10000, 0.05)]
    public void LocateRedistributesFewKeysWhenAddingNodes(int nodes, int sampleSize, double margin)
    {
        var shardManager = new ShardManager(Enumerable.Range(0, nodes));
        Func<int, int> hasher = x => HashCodeHelper.GetHashCode(x);
        var normal = new RendezvousConsistentHash<int>(shardManager, hasher);

        var shardManagerX = new ShardManager(Enumerable.Range(0, nodes + 1));
        var plusX = new RendezvousConsistentHash<int>(shardManagerX, hasher);

        var moved = Enumerable
            .Range(0, sampleSize)
            .Count(x => normal.Locate(x)!.Location != plusX.Locate(x)!.Location);

        var errorMargin = Math.Abs(((double)moved / sampleSize) - (1.0 / (nodes + 1)));
        Assert.True(errorMargin < margin, $"Distribution is not uniform, error margin: {errorMargin}");
    }
}
