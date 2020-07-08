// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.Cache.Monitor.App.Rules.Autoscaling;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Monitor.Test
{
    public class RedisScaleRelationTests : TestBase
    {
        public RedisScaleRelationTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void CanChangeShardsWithinSameTier()
        {
            foreach (var group in RedisClusterSize.Instances.GroupBy(size => size.Tier))
            {
                var instances = group.ToList();

                foreach (var inst1 in instances)
                {
                    foreach (var inst2 in instances)
                    {
                        Assert.True(RedisScalingUtilities.CanScale(inst1, inst2));
                    }
                }
            }
        }

        [Fact]
        public void CanScaleAcrossPremiumPlansWhenShardsRemainEqual()
        {
            foreach (var group in RedisClusterSize
                .Instances
                .Where(size => size.Tier.Plan == RedisPlan.Premium)
                .GroupBy(size => size.Shards))
            {
                var instances = group.ToList();

                foreach (var inst1 in instances)
                {
                    foreach (var inst2 in instances)
                    {
                        Assert.True(RedisScalingUtilities.CanScale(inst1, inst2));
                    }
                }
            }
        }

        [Theory]
        [InlineData("P1/1", "P2/1")]
        [InlineData("P1/1", "P3/1")]
        public void CanScaleBetween(string fromString, string toString)
        {
            var from = RedisClusterSize.TryParse(fromString).ThrowIfFailure();
            var to = RedisClusterSize.TryParse(toString).ThrowIfFailure();
            Assert.True(RedisScalingUtilities.CanScale(from, to));
        }
    }
}
