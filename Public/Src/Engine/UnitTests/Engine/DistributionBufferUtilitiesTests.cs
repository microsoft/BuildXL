// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Engine.Distribution;
using BuildXL.Utilities.Core;
using Xunit;

namespace Test.BuildXL.Engine
{
    public sealed class DistributionBufferUtilitiesTests
    {
        [Fact]
        public void SmallBufferIsReused()
        {
            var buffer = new MemoryStream();
            var counters = new CounterCollection<DistributionCounter>();
            buffer.SetLength(DistributionBufferUtilities.MaximumRetainedCapacity);
            MemoryStream original = buffer;

            DistributionBufferUtilities.Reset(ref buffer, counters, DistributionBufferKind.ExecutionLogEvents);

            Assert.Same(original, buffer);
            Assert.Equal(0, buffer.Length);
            Assert.Equal(DistributionBufferUtilities.MaximumRetainedCapacity, buffer.Capacity);
            Assert.Equal(1, counters.GetCounterValue(DistributionCounter.DistributionBufferResetCapacity1MBOrLess));
            Assert.Equal(0, counters.GetCounterValue(DistributionCounter.DistributionBufferReplacementCount));
        }

        [Fact]
        public void RepeatedOversizedBurstsDoNotRemainRetained()
        {
            var buffer = new MemoryStream();
            var counters = new CounterCollection<DistributionCounter>();
            int oversizedCapacity = DistributionBufferUtilities.MaximumRetainedCapacity * 8;

            for (int iteration = 0; iteration < 64; iteration++)
            {
                buffer.SetLength(oversizedCapacity);
                MemoryStream oversizedBuffer = buffer;

                DistributionBufferUtilities.Reset(ref buffer, counters, DistributionBufferKind.FlushedExecutionLog);

                Assert.NotSame(oversizedBuffer, buffer);
                Assert.False(oversizedBuffer.CanWrite);
                Assert.Equal(0, buffer.Length);
                Assert.True(buffer.Capacity <= DistributionBufferUtilities.MaximumRetainedCapacity);
            }

            Assert.Equal(64, counters.GetCounterValue(DistributionCounter.DistributionBufferResetCapacityGreaterThan4MB));
            Assert.Equal(64, counters.GetCounterValue(DistributionCounter.DistributionBufferReplacementCount));
            Assert.Equal(64L * oversizedCapacity, counters.GetCounterValue(DistributionCounter.DistributionBufferDiscardedCapacityBytes));
            Assert.Equal(64, counters.GetCounterValue(DistributionCounter.FlushedExecutionLogBufferReplacementCount));
            Assert.Equal(64L * oversizedCapacity, counters.GetCounterValue(DistributionCounter.FlushedExecutionLogBufferDiscardedCapacityBytes));
        }

        [Theory]
        [InlineData(1 << 16, DistributionCounter.DistributionBufferResetCapacity64KBOrLess)]
        [InlineData((1 << 16) + 1, DistributionCounter.DistributionBufferResetCapacity256KBOrLess)]
        [InlineData((1 << 18) + 1, DistributionCounter.DistributionBufferResetCapacity1MBOrLess)]
        [InlineData((1 << 20) + 1, DistributionCounter.DistributionBufferResetCapacity4MBOrLess)]
        [InlineData((1 << 22) + 1, DistributionCounter.DistributionBufferResetCapacityGreaterThan4MB)]
        public void ResetCapacityHistogramRecordsBoundaries(int capacity, DistributionCounter expectedCounter)
        {
            var buffer = new MemoryStream();
            var counters = new CounterCollection<DistributionCounter>();
            buffer.SetLength(capacity);

            DistributionBufferUtilities.Reset(ref buffer, counters, DistributionBufferKind.ManifestEvents);

            Assert.Equal(1, counters.GetCounterValue(expectedCounter));
        }

        [Theory]
        [InlineData(
            (int)DistributionBufferKind.ExecutionLogEvents,
            DistributionCounter.ExecutionLogEventBufferReplacementCount,
            DistributionCounter.ExecutionLogEventBufferDiscardedCapacityBytes)]
        [InlineData(
            (int)DistributionBufferKind.ManifestEvents,
            DistributionCounter.ManifestEventBufferReplacementCount,
            DistributionCounter.ManifestEventBufferDiscardedCapacityBytes)]
        [InlineData(
            (int)DistributionBufferKind.FlushedManifestEvents,
            DistributionCounter.FlushedManifestEventBufferReplacementCount,
            DistributionCounter.FlushedManifestEventBufferDiscardedCapacityBytes)]
        [InlineData(
            (int)DistributionBufferKind.FlushedExecutionLog,
            DistributionCounter.FlushedExecutionLogBufferReplacementCount,
            DistributionCounter.FlushedExecutionLogBufferDiscardedCapacityBytes)]
        public void OversizedReplacementTelemetryIsAttributedToBufferKind(
            int bufferKind,
            DistributionCounter replacementCountCounter,
            DistributionCounter discardedCapacityCounter)
        {
            var buffer = new MemoryStream();
            var counters = new CounterCollection<DistributionCounter>();
            int oversizedCapacity = DistributionBufferUtilities.MaximumRetainedCapacity + 1;
            buffer.SetLength(oversizedCapacity);

            DistributionBufferUtilities.Reset(ref buffer, counters, (DistributionBufferKind)bufferKind);

            Assert.Equal(1, counters.GetCounterValue(replacementCountCounter));
            Assert.Equal(oversizedCapacity, counters.GetCounterValue(discardedCapacityCounter));
        }
    }
}
