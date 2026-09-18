// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using BuildXL.Engine.Distribution;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Tracing;
using Xunit;

namespace Test.BuildXL.Distribution.Benchmarks
{
    /// <summary>
    /// Measures execution-log batching throughput and memory behavior while the notification sink is slower than
    /// event producers.
    /// </summary>
    /// <remarks>
    /// The benchmark writes 250,000 fixed-size events through <see cref="BinaryLogger"/> and compares several flush
    /// thresholds. The delayed sink makes notification frequency visible in elapsed time while allocation, queue depth,
    /// and retained buffer capacity expose the memory tradeoff of larger batches. Run it with:
    /// <c>bxl Test.BuildXL.Distribution.Benchmarks.dsc /p:[Sdk.BuildXL]runBenchmarks=1 /q:ReleaseNet10 /server-</c>.
    /// </remarks>
    public sealed class ExecutionLogBatchingBenchmarks
    {
        private const int EventCount = 250_000;
        private const int EventPayloadSize = 192;
        private const int SinkDelayMilliseconds = 2;

        private readonly ITestOutputHelper m_output;

        public ExecutionLogBatchingBenchmarks(ITestOutputHelper output)
        {
            m_output = output;
        }

        /// <summary>
        /// Compares execution-log flush thresholds from 64 KiB through 4 MiB under the same event workload.
        /// </summary>
        /// <remarks>
        /// Every run must send approximately the same number of bytes, and each larger threshold must reduce the number
        /// of sink notifications. This verifies that timing differences come from batching rather than dropped data.
        /// </remarks>
        [Fact]
        public void CompareExecutionLogBatchSizesUnderSlowSink()
        {
            // Include the previous 64 KiB threshold, the selected 1 MiB threshold, and adjacent tradeoff points.
            int[] thresholds =
            {
                64 << 10,
                256 << 10,
                1 << 20,
                4 << 20,
            };

            var measurements = new List<Measurement>(thresholds.Length);
            foreach (int threshold in thresholds)
            {
                Measurement measurement = Measure(threshold);
                measurements.Add(measurement);
                m_output.WriteLine(
                    $"{threshold / 1024,5:N0} KiB: {measurement.Elapsed.TotalMilliseconds,8:N1} ms, " +
                    $"{measurement.FlushCount,5:N0} flushes, {measurement.MaxPendingEvents,8:N0} max queued events, " +
                    $"{measurement.EventWriterFactoryCalls,8:N0} writers, " +
                    $"{measurement.AllocatedBytes / (1024.0 * 1024.0),8:N1} MiB allocated, " +
                    $"{measurement.PeakBufferedCapacity / (1024.0 * 1024.0),5:N1} MiB peak / " +
                    $"{measurement.RetainedBufferedCapacity / (1024.0 * 1024.0),4:N1} MiB retained buffers, " +
                    $"{measurement.BytesSent / (1024.0 * 1024.0),6:N1} MiB sent");
            }

            for (int i = 1; i < measurements.Count; i++)
            {
                Assert.True(measurements[i].FlushCount < measurements[i - 1].FlushCount);
                Assert.InRange(
                    measurements[i].BytesSent,
                    (long)(measurements[0].BytesSent * 0.99),
                    (long)(measurements[0].BytesSent * 1.01));
            }
        }

        /// <summary>
        /// Runs the complete event-production workload for one flush threshold and captures throughput and memory data.
        /// </summary>
        private static Measurement Measure(int threshold)
        {
            ForceCollection();

            var sink = new SlowNotificationSink();
            using var stream = new NotifyOrchestratorExecutionLogTarget.NotifyStream(sink.Notify, threshold);
            BuildXLContext context = BuildXLContext.CreateInstanceForTesting();
            var payload = new byte[EventPayloadSize];

            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            long maxPendingEvents;
            long eventWriterFactoryCalls;
            using (var logger = new BinaryLogger(
                stream,
                context,
                Guid.NewGuid(),
                closeStreamOnDispose: false,
                onEventWritten: stream.FlushIfNeeded))
            {
                for (int eventIndex = 0; eventIndex < EventCount; eventIndex++)
                {
                    using var eventScope = logger.StartEvent(eventId: 1, workerId: 0);
                    eventScope.Writer.Write(eventIndex);
                    eventScope.Writer.Write(payload);
                }

                logger.FlushAsync().GetAwaiter().GetResult();
                maxPendingEvents = logger.MaxPendingEventsCount;
                eventWriterFactoryCalls = logger.EventWriterFactoryCalls;
            }

            stopwatch.Stop();
            long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

            return new Measurement(
                stopwatch.Elapsed,
                sink.NotificationCount,
                maxPendingEvents,
                eventWriterFactoryCalls,
                allocatedBytes,
                sink.MaximumBufferedCapacity,
                stream.BufferedCapacity + sink.BufferedCapacity,
                sink.BytesSent);
        }

        private static void ForceCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        /// <summary>
        /// Models an orchestrator notification path that takes a fixed amount of time to consume each batch.
        /// </summary>
        private sealed class SlowNotificationSink
        {
            private MemoryStream m_outboundBuffer = new MemoryStream();

            public int NotificationCount { get; private set; }

            public int MaximumBufferedCapacity { get; private set; }

            public int BufferedCapacity => m_outboundBuffer.Capacity;

            public long BytesSent { get; private set; }

            /// <summary>
            /// Copies one notification into the outbound buffer, records its size, and applies the simulated sink delay.
            /// </summary>
            public void Notify(MemoryStream source)
            {
                source.WriteTo(m_outboundBuffer);
                NotificationCount++;
                BytesSent += m_outboundBuffer.Length;
                MaximumBufferedCapacity = Math.Max(
                    MaximumBufferedCapacity,
                    source.Capacity + m_outboundBuffer.Capacity);

                Thread.Sleep(SinkDelayMilliseconds);
                DistributionBufferUtilities.Reset(
                    ref m_outboundBuffer,
                    NotifyOrchestratorExecutionLogTarget.NotifyStream.MaximumRetainedBatchCapacity);
            }
        }

        /// <summary>
        /// Captures the elapsed time, batching activity, logger pressure, allocation, and buffer sizes for one threshold.
        /// </summary>
        private readonly struct Measurement
        {
            public Measurement(
                TimeSpan elapsed,
                int flushCount,
                long maxPendingEvents,
                long eventWriterFactoryCalls,
                long allocatedBytes,
                int peakBufferedCapacity,
                int retainedBufferedCapacity,
                long bytesSent)
            {
                Elapsed = elapsed;
                FlushCount = flushCount;
                MaxPendingEvents = maxPendingEvents;
                EventWriterFactoryCalls = eventWriterFactoryCalls;
                AllocatedBytes = allocatedBytes;
                PeakBufferedCapacity = peakBufferedCapacity;
                RetainedBufferedCapacity = retainedBufferedCapacity;
                BytesSent = bytesSent;
            }

            public TimeSpan Elapsed { get; }

            public int FlushCount { get; }

            public long MaxPendingEvents { get; }

            public long EventWriterFactoryCalls { get; }

            public long AllocatedBytes { get; }

            public int PeakBufferedCapacity { get; }

            public int RetainedBufferedCapacity { get; }

            public long BytesSent { get; }
        }
    }
}
