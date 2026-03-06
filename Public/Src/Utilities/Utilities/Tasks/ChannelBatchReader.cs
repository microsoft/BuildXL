// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

#nullable enable

namespace BuildXL.Utilities.Core.Tasks
{
    /// <summary>
    /// Owns a bounded <see cref="Channel{T}"/> of strings, accumulating writes into batches and
    /// dispatching each batch to a caller-supplied async callback in a background task.
    /// </summary>
    /// <remarks>
    /// Two flush triggers exist: a periodic timer and a size threshold.
    /// Call <see cref="Write"/> to enqueue items; call <see cref="Dispose"/> to flush remaining
    /// items and release all resources.
    /// </remarks>
    public sealed class ChannelBatchReader : IDisposable
    {
        /// <summary>
        /// Default maximum items per batch before an immediate flush is triggered.
        /// </summary>
        public const int DefaultMaxBatchSize = 5_000;

        /// <summary>
        /// Default bounded channel capacity.  Oldest items are dropped when the channel is full.
        /// </summary>
        public const int DefaultChannelCapacity = 500_000;

        /// <summary>
        /// Default time between periodic flushes.
        /// </summary>
        public static readonly TimeSpan DefaultFlushInterval = TimeSpan.FromSeconds(10);

        private readonly Channel<string> m_channel;
        private readonly Func<Stream, CancellationToken, Task> m_flushBatch;
        private readonly int m_maxBatchSize;
        private readonly TimeSpan m_flushInterval;
        private readonly CancellationTokenSource m_cts;
        private readonly Task m_loopTask;
        private readonly Action<string>? m_errorLogger;
        private int m_disposed;

        /// <summary>
        /// Constructs a new <see cref="ChannelBatchReader"/> and immediately starts the background flush loop.
        /// </summary>
        /// <remarks>
        /// Async callback invoked with each completed batch.  The second argument is a
        /// <see cref="CancellationToken"/> that is cancelled when <see cref="Dispose"/> is called;
        /// the final post-cancellation drain passes <see cref="CancellationToken.None"/> so remaining
        /// items are always flushed.  The callback should not throw; any exception will propagate. The
        /// callback does not own the stream and should not dispose it; the stream is only valid for the 
        /// duration of the callback and will be reset and reused for subsequent batches.
        /// out of <see cref="Dispose"/>.
        /// </remarks>
        public ChannelBatchReader(
            Func<Stream, CancellationToken, Task> flushBatch,
            int maxBatchSize = DefaultMaxBatchSize,
            int channelCapacity = DefaultChannelCapacity,
            TimeSpan? flushInterval = null,
            Action<string>? errorLogger = null)
        {
            m_flushBatch = flushBatch;
            m_maxBatchSize = maxBatchSize > 0
                ? maxBatchSize
                : throw new ArgumentOutOfRangeException(nameof(maxBatchSize), "Must be greater than zero.");
            m_flushInterval = flushInterval ?? DefaultFlushInterval;

            m_channel = Channel.CreateBounded<string>(new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

            m_cts = new CancellationTokenSource();
            m_loopTask = Task.Run(FlushLoopAsync);
            m_errorLogger = errorLogger;
        }

        /// <summary>
        /// Enqueues a string item for background processing.
        /// This method is non-blocking and thread-safe; oldest items are silently dropped when
        /// the channel is at capacity.
        /// </summary>
        public void Write(string line) => m_channel.Writer.TryWrite(line);

        /// <summary>
        /// Completes the channel writer, cancels the background loop, waits for the final drain
        /// flush to finish, then releases all resources.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            // Complete the writer first: the loop sees a closed channel and exits its delay
            // immediately rather than waiting for the next timer tick.
            m_channel.Writer.TryComplete();
            m_cts.Cancel();

            try
            {
                m_loopTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
            finally
            {
                m_cts.Dispose();
            }
        }

        private async Task FlushLoopAsync()
        {
            try
            {
                // We are going to reuse the same stream across all batches to take advantage of the internal buffer and avoid repeated allocations.
                // The provided flush callback is expected to behave appropriately.
                using var stream = new MemoryStream();
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true);
                int batchSize = 0;
                while (true)
                {
                    // Synchronously drain as many items as possible without blocking.
                    // TryRead returns false immediately when the channel is empty.
                    while (batchSize < m_maxBatchSize && m_channel.Reader.TryRead(out var item))
                    {
                        await writer.WriteLineAsync(item);
                        batchSize++;
                    }

                    if (batchSize > 0)
                    {
                        // Ensure all buffered text is written to the underlying MemoryStream.
                        await writer.FlushAsync();

                        // Move the stream position back to the beginning so the callback can read the whole batch
                        stream.Position = 0;
                        await m_flushBatch(stream, m_cts.Token);

                        // Reset the stream length to zero, discarding old data but keeping the
                        // internal buffer capacity for the next batch.
                        stream.SetLength(0);
                    }

                    if (batchSize >= m_maxBatchSize)
                    {
                        // Batch is at capacity: flush straight away and loop back to keep draining,
                        // skipping the timer wait entirely.
                        batchSize = 0;
                        continue;
                    }

                    if (batchSize > 0)
                    {
                        batchSize = 0;
                    }

                    // Channel is empty and batch is below threshold; sleep until the interval
                    // elapses or until Dispose cancels us, whichever comes first.
                    try
                    {
                        await Task.Delay(m_flushInterval, m_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                // Final drain: pick up any items that arrived concurrently during shutdown.
                while (m_channel.Reader.TryRead(out var item))
                {
                    await writer.WriteLineAsync(item);
                    batchSize++;
                }

                if (batchSize > 0)
                {
                    await writer.FlushAsync();

                    // Move the stream position back to the beginning so the callback can read the whole batch
                    stream.Position = 0;
                    // The CancellationToken is already cancelled here; pass None so the callback
                    // is not prevented from completing the final flush.
                    await m_flushBatch(stream, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                // Make sure to log any exceptions that escape the flush loop, as they would otherwise be silently swallowed by the Task scheduler and potentially lost during shutdown.
                m_errorLogger?.Invoke($"[ChannelBatchReader] Exception in flush loop: {ex}");
                throw;
            }
        }
    }
}
