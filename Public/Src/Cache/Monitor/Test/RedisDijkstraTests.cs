// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Xunit.Abstractions;
using BuildXL.Cache.Monitor.App.Rules.Autoscaling;
using FluentAssertions;

namespace BuildXL.Cache.Monitor.Test
{
    public class RedisDijkstraTests : TestBase
    {
        public RedisDijkstraTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void CanFindEmptyRoute()
        {
            var from = RedisClusterSize.Parse("P1/1");
            var to = RedisClusterSize.Parse("P1/1");
            var path = RedisScalingUtilities.ComputeShortestPath(from, to, size => size.ScaleEligibleSizes, (f, t) => 1);
            path.Should().BeEmpty();
        }

        [Fact]
        public void CanFindSingleRoute()
        {
            var from = RedisClusterSize.Parse("P1/1");
            var to = RedisClusterSize.Parse("P1/2");
            var path = RedisScalingUtilities.ComputeShortestPath(from, to, size => size.ScaleEligibleSizes, (f, t) => 1);
            path.Count.Should().Be(1);
            path[0].Should().Be(to);
        }

        [Fact]
        public void SucceedsOnSimpleRoute()
        {
            var from = RedisClusterSize.Parse("P1/1");
            var to = RedisClusterSize.Parse("P3/3");
            var path = RedisScalingUtilities.ComputeShortestPath(from, to, size => size.ScaleEligibleSizes, (f, t) => 1);
            path.Should().BeEquivalentTo(new RedisClusterSize[] { RedisClusterSize.Parse("P3/1"), RedisClusterSize.Parse("P3/3") });
        }

        [Fact]
        public void FailsOnNonExistantRoute()
        {
            var from = RedisClusterSize.Parse("P1/1");
            var to = RedisClusterSize.Parse("P3/3");
            var path = RedisScalingUtilities.ComputeShortestPath(from, to, size => new RedisClusterSize[] { }, (f, t) => 1);
            path.Should().BeEmpty();
        }
    }
}
