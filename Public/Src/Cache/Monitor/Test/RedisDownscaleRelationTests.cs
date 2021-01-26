// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Monitor.App.Rules.Autoscaling;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Monitor.Test
{
    public class RedisDownscaleRelationTests : TestBase
    {
        public RedisDownscaleRelationTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void RemovingShardsIsDownscaling()
        {
            foreach (var source in RedisClusterSize.Instances)
            {
                var candidates =
                    from size in source.ScaleEligibleSizes
                    where size.Tier.Equals(source.Tier) && size.Shards < source.Shards
                    select size;

                foreach (var to in candidates)
                {
                    RedisScalingUtilities.IsDownScale(source, to).Should().BeTrue();
                }
            }
        }

        [Fact]
        public void LoweringTierIsDownscaling()
        {
            foreach (var source in RedisClusterSize.Instances)
            {
                var candidates =
                    from size in source.ScaleEligibleSizes
                    where size.Shards == source.Shards && RedisScalingUtilities.IsDownScale(source.Tier, size.Tier)
                    select size;

                foreach (var to in candidates)
                {
                    RedisScalingUtilities.IsDownScale(source, to).Should().BeTrue();
                }
            }
        }

        [Theory]
        [InlineData("P1/2", "P1/1")]
        [InlineData("P2/2", "P1/2")]
        [InlineData("P3/10", "P2/4")]
        public void DownscaleManualExamples(string fromString, string toString)
        {
            var from = RedisClusterSize.TryParse(fromString).ThrowIfFailure();
            var to = RedisClusterSize.TryParse(toString).ThrowIfFailure();
            Assert.True(RedisScalingUtilities.IsDownScale(from, to));
        }
    }
}
