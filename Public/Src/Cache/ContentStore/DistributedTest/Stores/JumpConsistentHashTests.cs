// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Hashing;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Stores;

public class JumpConsistentHashTests
{
    [Fact]
    public void NewThrowsExceptionWhenLocationsIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new JumpConsistentHash<string>(Array.Empty<string>()));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    public void LocateMatchesJumpConsistentHashBehavior(int key, int expectedLocation)
    {
        var locations = new[] { 0, 1, 2 };
        var hash = new JumpConsistentHash<int>(locations);

        var result = hash.Locate(key);

        Assert.Equal(expectedLocation, result);
    }

    [Theory]
    [InlineData(5, 10000, 0.05)]
    [InlineData(10, 50000, 0.05)]
    [InlineData(100, 100000, 0.05)]
    public void LocateReturnsShardsWithRoughlyUniformDistribution(int nodes, int sampleSize, double margin)
    {
        var hash = new JumpConsistentHash<int>(Enumerable.Range(0, nodes).ToList());

        var results = Enumerable.Range(0, sampleSize)
            .Select(x => hash.Locate(x))
            .GroupBy(x => x)
            .Select(x => x.Count())
            .ToArray();

        foreach (var count in results)
        {
            var errorMargin = Math.Abs(((double)count / sampleSize) - (1.0 / hash.Locations.Count));
            Assert.True(errorMargin < margin, $"Distribution is not uniform, error margin: {errorMargin}");
        }
    }

    [Theory]
    [InlineData(5, 10000, 0.05)]
    public void LocateRedistributesFewKeysWhenAddingNodes(int nodes, int sampleSize, double margin)
    {
        var normal = new JumpConsistentHash<int>(Enumerable.Range(0, nodes).ToList());
        var plusX = new JumpConsistentHash<int>(Enumerable.Range(0, nodes + 1).ToList());

        var moved = Enumerable
            .Range(0, sampleSize)
            .Count(x => normal.Locate(x) != plusX.Locate(x));

        var errorMargin = Math.Abs(((double)moved / sampleSize) - (1.0 / plusX.Locations.Count));
        Assert.True(errorMargin < margin, $"Distribution is not uniform, error margin: {errorMargin}");
    }

    [Fact]
    public void DoesntOverflowOnExtremelySmallRandomNumber()
    {
        var example = new ShortHash("VSO0:0BCD64D0A8FCFFAEAD1D51");
        var key = BlobCacheShardingKey.FromShortHash(example);
        var ring = new JumpConsistentHash<int>(Enumerable.Range(0, 100).ToList());
        var location = ring.Locate(key.Key);
        Assert.Equal(21, location);
    }
}
