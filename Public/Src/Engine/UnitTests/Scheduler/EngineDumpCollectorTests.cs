// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Scheduler;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    public class EngineDumpCollectorTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public EngineDumpCollectorTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void DisabledTriggerDoesNotDump()
        {
            var collector = CreateCollector(EngineDumpTrigger.Disabled);
            Assert.False(collector.IsEnabled);
            Assert.False(collector.HasDumped);

            // Should be a no-op, no exception thrown.
            collector.CheckTriggerAndDump(processMemoryMb: 10000, elapsedSeconds: 9999, buildPercentage: 100);
            Assert.False(collector.HasDumped);
            Assert.Equal(0, collector.DumpCallCount);
        }

        [Fact]
        public void MemoryTriggerDoesNotFireBelowThreshold()
        {
            var trigger = new EngineDumpTrigger(EngineDumpTriggerKind.MemoryMb, 8000);
            var collector = CreateCollector(trigger);

            collector.CheckTriggerAndDump(processMemoryMb: 7999, elapsedSeconds: 0, buildPercentage: 0);
            Assert.False(collector.HasDumped);
            Assert.Equal(0, collector.DumpCallCount);
        }

        [Fact]
        public void TimeTriggerDoesNotFireBelowThreshold()
        {
            var trigger = new EngineDumpTrigger(EngineDumpTriggerKind.TimeSec, 600);
            var collector = CreateCollector(trigger);

            collector.CheckTriggerAndDump(processMemoryMb: 0, elapsedSeconds: 599, buildPercentage: 0);
            Assert.False(collector.HasDumped);
            Assert.Equal(0, collector.DumpCallCount);
        }

        [Fact]
        public void PercentageTriggerDoesNotFireBelowThreshold()
        {
            var trigger = new EngineDumpTrigger(EngineDumpTriggerKind.BuildPercentage, 50);
            var collector = CreateCollector(trigger);

            collector.CheckTriggerAndDump(processMemoryMb: 0, elapsedSeconds: 0, buildPercentage: 49);
            Assert.False(collector.HasDumped);
            Assert.Equal(0, collector.DumpCallCount);
        }

        [Fact]
        public void MemoryTriggerFiresAtThreshold()
        {
            var trigger = new EngineDumpTrigger(EngineDumpTriggerKind.MemoryMb, 8000);
            var collector = CreateCollector(trigger);

            collector.CheckTriggerAndDump(processMemoryMb: 8000, elapsedSeconds: 0, buildPercentage: 0);
            Assert.True(collector.HasDumped);
            Assert.Equal(1, collector.DumpCallCount);
        }

        [Fact]
        public void TimeTriggerFiresAtThreshold()
        {
            var trigger = new EngineDumpTrigger(EngineDumpTriggerKind.TimeSec, 600);
            var collector = CreateCollector(trigger);

            collector.CheckTriggerAndDump(processMemoryMb: 0, elapsedSeconds: 600, buildPercentage: 0);
            Assert.True(collector.HasDumped);
            Assert.Equal(1, collector.DumpCallCount);
        }

        [Fact]
        public void PercentageTriggerFiresAtThreshold()
        {
            var trigger = new EngineDumpTrigger(EngineDumpTriggerKind.BuildPercentage, 50);
            var collector = CreateCollector(trigger);

            collector.CheckTriggerAndDump(processMemoryMb: 0, elapsedSeconds: 0, buildPercentage: 50);
            Assert.True(collector.HasDumped);
            Assert.Equal(1, collector.DumpCallCount);
        }

        [Fact]
        public void OnlyFiresOnce()
        {
            var trigger = new EngineDumpTrigger(EngineDumpTriggerKind.BuildPercentage, 50);
            var collector = CreateCollector(trigger);

            collector.CheckTriggerAndDump(processMemoryMb: 0, elapsedSeconds: 0, buildPercentage: 50);
            Assert.True(collector.HasDumped);

            // Second call should not attempt another dump (one-shot guarantee).
            collector.CheckTriggerAndDump(processMemoryMb: 0, elapsedSeconds: 0, buildPercentage: 100);
            Assert.True(collector.HasDumped);
            Assert.Equal(1, collector.DumpCallCount);
        }

        private MockEngineDumpCollector CreateCollector(EngineDumpTrigger trigger)
        {
            var loggingContext = CreateLoggingContextForTest();
            string tempDir = Path.Combine(Path.GetTempPath(), "EngineDumpCollectorTests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            return new MockEngineDumpCollector(trigger, tempDir, loggingContext);
        }

        /// <summary>
        /// Test subclass that overrides CaptureDump to avoid spawning expensive dump processes.
        /// Tracks how many times the trigger logic decided to dump.
        /// </summary>
        private sealed class MockEngineDumpCollector : EngineDumpCollector
        {
            public int DumpCallCount { get; private set; }

            public MockEngineDumpCollector(EngineDumpTrigger trigger, string logsDirectory, LoggingContext loggingContext)
                : base(trigger, logsDirectory, loggingContext)
            {
            }

            protected override void CaptureDump()
            {
                DumpCallCount++;
            }
        }
    }
}
