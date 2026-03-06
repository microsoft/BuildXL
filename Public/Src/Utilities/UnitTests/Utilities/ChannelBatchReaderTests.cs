// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Core.Tasks;
using Xunit;

#nullable enable

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests for <see cref="ChannelBatchReader"/>.
    /// </summary>
    public class ChannelBatchReaderTests
    {
        [Fact]
        public void DisposeFlushesAllWrites()
        {
            var received = new List<string>();

            using (var cbr = new ChannelBatchReader(
                async (stream, ct) =>
                {
                    received.AddRange(ReadLines(stream));
                    await Task.CompletedTask;
                },
                maxBatchSize: 100,
                channelCapacity: 1000,
                flushInterval: TimeSpan.FromHours(1))) // long interval – only Dispose triggers flush
            {
                for (int i = 0; i < 10; i++)
                {
                    cbr.Write($"line-{i}");
                }
            }

            Assert.Equal(10, received.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Contains($"line-{i}", received);
            }
        }

        [Fact]
        public void EmptyChannelDisposeDoesNotInvokeCallback()
        {
            int callCount = 0;

            using (var cbr = new ChannelBatchReader(
                async (stream, ct) =>
                {
                    callCount++;
                    await Task.CompletedTask;
                },
                maxBatchSize: 100,
                channelCapacity: 1000,
                flushInterval: TimeSpan.FromHours(1)))
            {
                // Write nothing
            }

            Assert.Equal(0, callCount);
        }

        [Fact]
        public void BatchSizeTriggerFlushesWithoutWaitingForTimer()
        {
            var batchSizes = new List<int>();
            int totalReceived = 0;
            var allFlushed = new TaskCompletionSource<bool>();

            var cbr = new ChannelBatchReader(
                async (stream, ct) =>
                {
                    var lines = ReadLines(stream);
                    lock (batchSizes)
                    {
                        batchSizes.Add(lines.Count);
                        totalReceived += lines.Count;
                        if (totalReceived >= 15)
                        {
                            allFlushed.TrySetResult(true);
                        }
                    }
                    await Task.CompletedTask;
                },
                maxBatchSize: 5,
                channelCapacity: 1000,
                flushInterval: TimeSpan.FromHours(1)); // timer should never fire during this test

            for (int i = 0; i < 15; i++)
            {
                cbr.Write($"item-{i}");
            }

            Assert.True(allFlushed.Task.Wait(TimeSpan.FromSeconds(10)), "Timed out waiting for all batches to flush.");
            cbr.Dispose();

            Assert.Equal(15, totalReceived);
            Assert.All(batchSizes, size => Assert.True(size <= 5, $"Batch size {size} exceeds maxBatchSize 5."));
        }

        [Fact]
        public async Task TimerTriggerFlushesWithoutDispose()
        {
            var flushed = new TaskCompletionSource<List<string>>();

            using var cbr = new ChannelBatchReader(
                async (stream, ct) =>
                {
                    flushed.TrySetResult(ReadLines(stream));
                    await Task.CompletedTask;
                },
                maxBatchSize: 1000,
                channelCapacity: 1000,
                flushInterval: TimeSpan.FromMilliseconds(200));

            cbr.Write("hello");
            cbr.Write("world");

            var result = await flushed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, result.Count);
            Assert.Contains("hello", result);
            Assert.Contains("world", result);
        }

        [Fact]
        public void BatchCallbackReceivesNewlineSeparatedContent()
        {
            string? rawContent = null;

            using (var cbr = new ChannelBatchReader(
                async (stream, ct) =>
                {
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    rawContent = await reader.ReadToEndAsync();
                },
                maxBatchSize: 100,
                channelCapacity: 1000,
                flushInterval: TimeSpan.FromHours(1)))
            {
                cbr.Write("alpha");
                cbr.Write("beta");
                cbr.Write("gamma");
            }

            Assert.NotNull(rawContent);
            var lines = rawContent!.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                   .Select(l => l.Trim())
                                   .ToArray();
            Assert.Contains("alpha", lines);
            Assert.Contains("beta", lines);
            Assert.Contains("gamma", lines);
        }

        [Fact]
        public void WriteAfterDisposeDoesNotThrow()
        {
            var cbr = new ChannelBatchReader(
                async (stream, ct) => await Task.CompletedTask,
                maxBatchSize: 100,
                channelCapacity: 1000);

            cbr.Dispose();
            cbr.Write("late write"); // TryWrite silently drops when the channel is completed
        }

        [Fact]
        public void ErrorLoggerIsCalledWhenCallbackThrows()
        {
            string? loggedError = null;
            using var errorLogged = new ManualResetEventSlim(false);

            var cbr = new ChannelBatchReader(
                async (stream, ct) =>
                {
                    await Task.CompletedTask;
                    throw new InvalidOperationException("Simulated callback failure");
                },
                maxBatchSize: 100,
                channelCapacity: 1000,
                flushInterval: TimeSpan.FromHours(1),
                errorLogger: msg =>
                {
                    loggedError = msg;
                    errorLogged.Set();
                });

            cbr.Write("trigger");

            // Wait for the background loop to pick up the item, flush, and fail
            Assert.True(errorLogged.Wait(TimeSpan.FromSeconds(10)), "Timed out waiting for error logger to be called.");

            // Dispose will surface the faulted loop task; swallow the expected exception
            try { cbr.Dispose(); } catch (InvalidOperationException) { }

            Assert.NotNull(loggedError);
            Assert.Contains("ChannelBatchReader", loggedError);
            Assert.Contains("Simulated callback failure", loggedError);
        }

        [Fact]
        public void LargeWriteVolumeAllReceivedOnDispose()
        {
            const int Count = 10_000;
            var received = new List<string>();

            using (var cbr = new ChannelBatchReader(
                async (stream, ct) =>
                {
                    var lines = ReadLines(stream);
                    lock (received)
                    {
                        received.AddRange(lines);
                    }
                    await Task.CompletedTask;
                },
                maxBatchSize: 500,
                channelCapacity: ChannelBatchReader.DefaultChannelCapacity,
                flushInterval: TimeSpan.FromHours(1)))
            {
                for (int i = 0; i < Count; i++)
                {
                    cbr.Write($"msg-{i}");
                }
            }

            Assert.Equal(Count, received.Count);
        }

        /// <summary>
        /// Reads all newline-delimited strings from a stream without closing it.
        /// </summary>
        private static List<string> ReadLines(Stream stream)
        {
            var lines = new List<string>();
            using var reader = new StreamReader(stream, leaveOpen: true);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                lines.Add(line);
            }
            return lines;
        }
    }
}
