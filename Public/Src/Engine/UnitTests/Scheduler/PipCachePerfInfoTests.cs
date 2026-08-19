// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Core;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    public class PipCachePerfInfoTests
    {
        [Fact]
        public void CacheLookupPerformanceInfoIsCreatedExplicitly()
        {
            var performance = new RunnablePipPerformanceInfo(DateTime.UtcNow);
            Assert.Null(performance.CacheLookupPerfInfo);

            PipCachePerfInfo cacheInfo = performance.GetOrCreateCacheLookupPerfInfo();

            Assert.Same(cacheInfo, performance.GetOrCreateCacheLookupPerfInfo());
            Assert.Same(cacheInfo, performance.CacheLookupPerfInfo);
        }

        [Fact]
        public void AccumulatesExactDurationsAndOccurrences()
        {
            var cacheInfo = new PipCachePerfInfo();
            OperationKind operationKind = OperationKind.GetTrackedCacheOperationKind(0);

            cacheInfo.LogCacheLookupStep(PipExecutionStep.CacheLookup, operationKind, TimeSpan.FromTicks(9_000));
            cacheInfo.LogCacheLookupStep(PipExecutionStep.CacheLookup, operationKind, TimeSpan.FromTicks(9_000));
            cacheInfo.LogCacheLookupStep(PipExecutionStep.ExecuteProcess, operationKind, TimeSpan.FromTicks(7_000));

            AssertCounter(cacheInfo.GetBeforeExecutionCounter(operationKind), operationKind, 18_000, 2);
            AssertCounter(cacheInfo.GetAfterExecutionCounter(operationKind), operationKind, 7_000, 1);
        }

        [Fact]
        public void RejectsUntrackedOperationKind()
        {
            var cacheInfo = new PipCachePerfInfo();
            OperationKind untrackedOperationKind = PipExecutionStep.ExecuteProcess;

            Assert.Throws<ArgumentException>(
                () => cacheInfo.LogCacheLookupStep(PipExecutionStep.CacheLookup, untrackedOperationKind, TimeSpan.FromTicks(1)));
        }

        [Fact]
        public void ConcurrentUpdatesAreAtomic()
        {
            const int UpdateCount = 10_000;
            const long DurationTicks = 9;
            var cacheInfo = new PipCachePerfInfo();
            OperationKind operationKind = OperationKind.GetTrackedCacheOperationKind(0);

            Parallel.For(
                0,
                UpdateCount,
                _ => cacheInfo.LogCacheLookupStep(PipExecutionStep.CacheLookup, operationKind, TimeSpan.FromTicks(DurationTicks)));

            AssertCounter(cacheInfo.GetBeforeExecutionCounter(operationKind), operationKind, UpdateCount * DurationTicks, UpdateCount);
        }

        [Fact]
        public void SaturatesDurationAndOccurrences()
        {
            var cacheInfo = new PipCachePerfInfo();
            OperationKind operationKind = OperationKind.GetTrackedCacheOperationKind(0);

            for (long i = 0; i <= PipCachePerfInfo.MaxRepresentableOccurrences; i++)
            {
                cacheInfo.LogCacheLookupStep(PipExecutionStep.CacheLookup, operationKind, TimeSpan.MaxValue);
            }

            AssertCounter(
                cacheInfo.GetBeforeExecutionCounter(operationKind),
                operationKind,
                PipCachePerfInfo.MaxRepresentableDurationTicks,
                PipCachePerfInfo.MaxRepresentableOccurrences);
        }

        [Fact]
        public void SerializationRoundTripsPackedCounters()
        {
            var cacheInfo = new PipCachePerfInfo();
            OperationKind operationKind = OperationKind.GetTrackedCacheOperationKind(0);
            cacheInfo.LogCacheLookupStep(PipExecutionStep.CacheLookup, operationKind, TimeSpan.FromTicks(12_345));
            cacheInfo.LogCacheLookupStep(PipExecutionStep.ExecuteProcess, operationKind, TimeSpan.FromTicks(67_890));

            using var stream = new MemoryStream();
            using (var writer = new BuildXLWriter(debug: false, stream, leaveOpen: true, logStats: false))
            {
                cacheInfo.Serialize(writer);
            }

            stream.Position = 0;
            using var reader = new BuildXLReader(debug: false, stream, leaveOpen: true);
            PipCachePerfInfo deserialized = PipCachePerfInfo.Deserialize(reader);

            AssertCounter(deserialized.GetBeforeExecutionCounter(operationKind), operationKind, 12_345, 1);
            AssertCounter(deserialized.GetAfterExecutionCounter(operationKind), operationKind, 67_890, 1);
        }

        [Fact]
        public void DeserializationIgnoresUnsupportedTrailingCounters()
        {
            int serializedCounterCount = OperationKind.TrackedCacheLookupCounterCount + 2;
            using var stream = new MemoryStream();
            using (var writer = new BuildXLWriter(debug: false, stream, leaveOpen: true, logStats: false))
            {
                WriteCounters(writer, serializedCounterCount, durationTicks: 123, occurrences: 4);
                WriteCounters(writer, serializedCounterCount, durationTicks: 567, occurrences: 8);
                writer.Write((byte)PipCacheMissType.Hit);
                writer.WriteCompact(11);
                writer.WriteCompact(13);
                writer.WriteCompact(17);
            }

            stream.Position = 0;
            using var reader = new BuildXLReader(debug: false, stream, leaveOpen: true);
            PipCachePerfInfo deserialized = PipCachePerfInfo.Deserialize(reader);

            OperationKind operationKind = OperationKind.GetTrackedCacheOperationKind(0);
            AssertCounter(deserialized.GetBeforeExecutionCounter(operationKind), operationKind, 123, 4);
            AssertCounter(deserialized.GetAfterExecutionCounter(operationKind), operationKind, 567, 8);
            Assert.Equal(PipCacheMissType.Hit, deserialized.CacheMissType);
            Assert.Equal(11, deserialized.NumPathSetsDownloaded);
            Assert.Equal(13, deserialized.NumCacheEntriesVisited);
            Assert.Equal(17, deserialized.NumCacheEntriesAbsent);
        }

        private static void AssertCounter(PipCachePerfInfo.CacheStepCounter counter, OperationKind operationKind, long durationTicks, long occurrences)
        {
            Assert.Equal(operationKind, counter.OperationKind);
            Assert.Equal(durationTicks, counter.DurationTicks);
            Assert.Equal(occurrences, counter.Occurrences);
        }

        private static void WriteCounters(BuildXLWriter writer, int count, long durationTicks, long occurrences)
        {
            writer.WriteCompact(count);
            for (int i = 0; i < count; i++)
            {
                writer.WriteCompact(durationTicks);
                writer.WriteCompact(occurrences);
            }
        }
    }
}
