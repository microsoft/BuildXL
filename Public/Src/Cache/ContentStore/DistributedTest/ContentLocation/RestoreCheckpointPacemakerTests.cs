using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Text;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation
{
    public class RestoreCheckpointPacemakerTests
    {
        [Theory]
        [InlineData(10)]
        public void SampledIdentifiersAreBetween0And1(int numSamples)
        {
            var checkpointTime = DateTime.UtcNow;
            for (var i = 0; i < numSamples; ++i)
            {
                var sample = RestoreCheckpointPacemaker.SampleIdentifier(ThreadSafeRandom.GetBytes(10), checkpointTime);
                sample.Should().BeInRange(0, 1);
            }
        }

        [Theory]
        [InlineData(10)]
        public void SampledIdentifiersDontChangeUnlessCheckpointTimeDoes(int numSamples)
        {
            for (var i = 0; i < numSamples; ++i)
            {
                var checkpointTime = DateTime.UtcNow;
                var machine = ThreadSafeRandom.GetBytes(10);

                var sample1 = RestoreCheckpointPacemaker.SampleIdentifier(machine, checkpointTime);
                var sample2 = RestoreCheckpointPacemaker.SampleIdentifier(machine, checkpointTime);
                sample1.Should().Be(sample2);

                var checkpointTime2 = checkpointTime + TimeSpan.FromSeconds(1);
                var sample3 = RestoreCheckpointPacemaker.SampleIdentifier(machine, checkpointTime2);
                var sample4 = RestoreCheckpointPacemaker.SampleIdentifier(machine, checkpointTime2);

                sample1.Should().NotBe(sample3);
                sample3.Should().Be(sample4);
            }
        }

        [Theory]
        [InlineData(10, 10)]
        public void SampledBucketIsInRange(int numSamples, uint buckets)
        {
            var gen = ThreadSafeRandom.Generator;
            for (var i = 0; i < numSamples; ++i)
            {
                var bucket = RestoreCheckpointPacemaker.SampleBucket(buckets, gen.NextDouble());
                bucket.Should().BeInRange(0, buckets);
            }
        }

        [Fact]
        public void RestoreTimeIsLinear()
        {
            var createCheckpointInterval = TimeSpan.FromMinutes(10);
            var heartbeatInterval = TimeSpan.FromMinutes(1);
            var restoreDelta = RestoreCheckpointPacemaker.ComputeRestoreTime(createCheckpointInterval, 10, 1);
            restoreDelta.Should().Be(TimeSpan.FromMinutes(1));
        }
    }
}
