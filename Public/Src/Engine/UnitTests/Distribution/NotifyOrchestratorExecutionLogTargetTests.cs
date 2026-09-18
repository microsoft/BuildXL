// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Engine.Distribution;
using Xunit;

namespace Test.BuildXL.Distribution
{
    /// <summary>
    /// Verifies execution-log batching, delivery, and buffer-retention behavior for orchestrator notifications.
    /// </summary>
    /// <remarks>
    /// These tests exercise <see cref="NotifyOrchestratorExecutionLogTarget.NotifyStream"/> with small configurable
    /// thresholds so boundary and failure behavior can be validated without producing production-sized payloads.
    /// </remarks>
    public sealed class NotifyOrchestratorExecutionLogTargetTests
    {
        /// <summary>
        /// Verifies that buffered events flush exactly when the configured size threshold is reached.
        /// </summary>
        [Fact]
        public void FlushesAtThresholdBoundary()
        {
            Assert.Equal(1 << 20, NotifyOrchestratorExecutionLogTarget.NotifyStream.BatchSizeThreshold);
            Assert.Equal(8 << 20, NotifyOrchestratorExecutionLogTarget.NotifyStream.MaximumRetainedBatchCapacity);

            var batches = new List<byte[]>();
            using var stream = new NotifyOrchestratorExecutionLogTarget.NotifyStream(
                buffer => batches.Add(buffer.ToArray()),
                batchSizeThreshold: 8);

            stream.Write(new byte[7], 0, 7);
            stream.FlushIfNeeded();
            Assert.Empty(batches);

            stream.WriteByte(7);
            stream.FlushIfNeeded();

            Assert.Single(batches);
            Assert.Equal(8, batches[0].Length);
            Assert.Equal(1, stream.NumFlushes);
            Assert.Equal(0, stream.BufferedLength);
        }

        /// <summary>
        /// Verifies that threshold flushes and the final close-time flush preserve byte order and residual data.
        /// </summary>
        [Fact]
        public void PreservesOrderAcrossThresholdAndCloseFlushes()
        {
            var batches = new List<byte[]>();
            var expected = Enumerable.Range(0, 10).Select(value => (byte)value).ToArray();
            var stream = new NotifyOrchestratorExecutionLogTarget.NotifyStream(
                buffer => batches.Add(buffer.ToArray()),
                batchSizeThreshold: 3);

            foreach (byte value in expected)
            {
                stream.WriteByte(value);
                stream.FlushIfNeeded();
            }

            stream.Close();
            stream.Close();

            Assert.Equal(new[] { 3, 3, 3, 1 }, batches.Select(batch => batch.Length));
            Assert.Equal(expected, batches.SelectMany(batch => batch));
        }

        /// <summary>
        /// Verifies that a failed notification leaves buffered events intact for a later retry.
        /// </summary>
        [Fact]
        public void FailedFlushRetainsEventsForRetry()
        {
            int attempts = 0;
            byte[] sent = null;
            using var stream = new NotifyOrchestratorExecutionLogTarget.NotifyStream(
                buffer =>
                {
                    if (attempts++ == 0)
                    {
                        throw new IOException("Simulated notification failure");
                    }

                    sent = buffer.ToArray();
                },
                batchSizeThreshold: 4);

            stream.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
            Assert.Throws<IOException>(() => stream.FlushIfNeeded());
            Assert.Equal(4, stream.BufferedLength);

            stream.Flush();

            Assert.Equal(new byte[] { 1, 2, 3, 4 }, sent);
            Assert.Equal(0, stream.BufferedLength);
        }

        /// <summary>
        /// Verifies that deactivation rejects new writes without discarding data that was already buffered.
        /// </summary>
        [Fact]
        public void DeactivationStopsAdditionalWrites()
        {
            byte[] sent = null;
            using var stream = new NotifyOrchestratorExecutionLogTarget.NotifyStream(
                buffer => sent = buffer.ToArray(),
                batchSizeThreshold: 8);

            stream.Write(new byte[] { 1, 2 }, 0, 2);
            stream.Deactivate();
            stream.Write(new byte[] { 3, 4 }, 0, 2);

            Assert.Equal(2, stream.BufferedLength);
            Assert.Null(sent);

            stream.Flush();
            Assert.Equal(new byte[] { 1, 2 }, sent);
        }

        /// <summary>
        /// Verifies that routine multi-megabyte growth is retained for reuse instead of being reallocated each batch.
        /// </summary>
        [Fact]
        public void RetainsRoutineExpandedBatchBuffer()
        {
            int payloadLength = NotifyOrchestratorExecutionLogTarget.NotifyStream.BatchSizeThreshold * 3;
            using var stream = new NotifyOrchestratorExecutionLogTarget.NotifyStream(
                _ => { },
                batchSizeThreshold: 64);

            stream.Write(new byte[payloadLength], 0, payloadLength);
            stream.FlushIfNeeded();

            Assert.Equal(0, stream.BufferedLength);
            Assert.InRange(
                stream.BufferedCapacity,
                payloadLength,
                NotifyOrchestratorExecutionLogTarget.NotifyStream.MaximumRetainedBatchCapacity);
        }

        /// <summary>
        /// Verifies that repeated oversized payloads are delivered but their expanded backing buffers are not retained.
        /// </summary>
        [Fact]
        public void RepeatedLargeBurstsDoNotRetainOversizedBuffers()
        {
            int oversizedLength = NotifyOrchestratorExecutionLogTarget.NotifyStream.MaximumRetainedBatchCapacity * 2;
            var burst = new byte[oversizedLength];
            int batches = 0;
            using var stream = new NotifyOrchestratorExecutionLogTarget.NotifyStream(
                buffer =>
                {
                    Assert.Equal(oversizedLength, buffer.Length);
                    batches++;
                },
                batchSizeThreshold: 64);

            for (int iteration = 0; iteration < 4; iteration++)
            {
                stream.Write(burst, 0, burst.Length);
                stream.FlushIfNeeded();

                Assert.Equal(0, stream.BufferedLength);
                Assert.True(stream.BufferedCapacity <= NotifyOrchestratorExecutionLogTarget.NotifyStream.MaximumRetainedBatchCapacity);
            }

            Assert.Equal(4, batches);
        }
    }
}
