// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.ParallelAlgorithms;
using static BuildXL.Utilities.FormattableStringEx;

#nullable enable

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Asynchronous logging queue which queues log messages and processes them on a dedicated logging thread.
    /// </summary>
    public sealed class LoggingQueue : ILoggingQueue
    {
        private readonly Action<LoggingContext, Dictionary<string, long>> m_traceAsyncLoggingStats;

        /// <summary>
        /// Indicates whether async logging is currently enabled
        /// </summary>
        private volatile bool m_isAsyncLoggingEnabled = false;

        /// <summary>
        /// Logging context for logging statistics about async logging upon completion
        /// </summary>
        private LoggingContext? m_loggingContext;

        /// <summary>
        /// Counters for events. Only counters for logged events are actually created
        /// </summary>
        private readonly EventCounter?[] m_counters = new EventCounter?[ushort.MaxValue];

        /// <summary>
        /// Queue of log actions and associated counters
        /// </summary>
        private readonly Channel<(EventCounter, Action)> m_logActionChannel;

        private readonly Task m_logActionTask;

        [MemberNotNullWhen(true, nameof(m_loggingContext))]
        private bool AsyncLoggingEnabled => m_isAsyncLoggingEnabled;

        private long m_totalLoggingQueueAddDurationMs;

        /// <nodoc />
        public LoggingQueue(Action<LoggingContext, Dictionary<string, long>> traceAsyncLoggingStats)
        {
            m_traceAsyncLoggingStats = traceAsyncLoggingStats;

            // Creating an channel and the log processing task even if the async logging would be off (which is unlikely).
            m_logActionChannel = Channel.CreateUnbounded<(EventCounter, Action)>(
                new UnboundedChannelOptions() {AllowSynchronousContinuations = true, SingleReader = true, SingleWriter = false,});
            m_logActionTask = CreateDrainLoggingQueueTask();
        }

        /// <inheritdoc />
        public void EnqueueLogAction(int eventId, Action logAction, string? eventName)
        {
            Contract.RequiresNotNull(logAction);

            var eventCounter = GetEventCounter(eventId, eventName);

            if (AsyncLoggingEnabled)
            {
                var stopwatch = StopwatchSlim.Start();
                
                try
                {
                    m_logActionChannel.Writer.TryWrite((eventCounter, logAction));
                    return;
                }
                finally
                {
                    Interlocked.Add(ref m_totalLoggingQueueAddDurationMs, (long)stopwatch.Elapsed.TotalMilliseconds);
                }
            }

            MeasuredLog(eventCounter, logAction);
        }

        /// <summary>
        /// Logs the event using the given <paramref name="logAction"/> and measures it duration
        /// </summary>
        private static void MeasuredLog(EventCounter eventCounter, Action logAction, bool threadSafe = false)
        {
            using (eventCounter.Measure(threadSafe))
            {
                logAction();
            }
        }

        /// <summary>
        /// Gets or creates the counter for a specific event
        /// </summary>
        private EventCounter GetEventCounter(int eventId, string? eventName)
        {
            Contract.Requires(eventId < m_counters.Length);
            var counter = m_counters[eventId];
            if (counter == null)
            {
                counter = new EventCounter(eventId, eventName);
                m_counters[eventId] = counter;
            }

            return counter;
        }

        /// <inheritdoc />
        public IDisposable EnterAsyncLoggingScope(LoggingContext loggingContext)
        {
            Contract.Requires(!m_isAsyncLoggingEnabled, "EnterAsyncLoggingScope must only be called once");
            m_loggingContext = loggingContext;
            return new AsyncLoggingScope(this);
        }

        private Task CreateDrainLoggingQueueTask()
        {
            return Task.Run(
                async () =>
                {
                    while (await m_logActionChannel.WaitToReadOrCanceledAsync(CancellationToken.None).ConfigureAwait(false))
                    {
                        while (m_logActionChannel.Reader.TryRead(out var item))
                        {
                            MeasuredLog(item.Item1, item.Item2, threadSafe: true);
                        }
                    }
                });
        }

        /// <summary>
        /// Completes async logging and waits for all the messages to be processed.
        /// </summary>
        private void CompleteAsyncLoggingAndWaitForCompletion()
        {
            var completionDurationTracker = StopwatchSlim.Start();

            if (!AsyncLoggingEnabled)
            {
                return;
            }

            m_isAsyncLoggingEnabled = false;

            m_logActionChannel.Writer.Complete();

            // Waiting for all the events to be processed.
            m_logActionTask.GetAwaiter().GetResult();

            TraceStatistics(m_loggingContext, completionDurationTracker.Elapsed);
        }
        
        private void TraceStatistics(LoggingContext loggingContext, TimeSpan asyncLoggingOverhang)
        {
            // Compute statistics about async logging
            var statistics = new Dictionary<string, long>();

            statistics["AsyncLoggingOverhangMs"] = (long)asyncLoggingOverhang.TotalMilliseconds;
            statistics["AddLoggingQueueOverheadMs"] = m_totalLoggingQueueAddDurationMs;
            long totalOccurrences = 0;
            TimeSpan totalDuration = TimeSpan.Zero;

            foreach (var counter in m_counters)
            {
                if (counter != null)
                {
                    totalOccurrences += counter.Occurrences;
                    totalDuration += counter.Elapsed;

                    statistics[counter.Name + ".Occurrences"] = counter.Occurrences;
                    statistics[counter.Name + ".DurationMs"] = (long)counter.Elapsed.TotalMilliseconds;
                }
            }

            statistics["TotalOccurrences"] = totalOccurrences;
            statistics["TotalDurationMs"] = (long)totalDuration.TotalMilliseconds;

            m_traceAsyncLoggingStats(loggingContext, statistics);
        }

        /// <summary>
        /// Tracks duration and occurrences for statistics
        /// </summary>
        private class EventCounter
        {
            public int Id { get; }
            public string Name { get; }

            private long m_elapsedTicks;
            private long m_occurrences;

            public EventCounter(int eventId, string? eventName)
            {
                Id = eventId;
                string eventIdText = eventId.ToString().PadLeft(4, '0');
                if (eventName != null && eventName.Length > 70)
                {
                    // Truncate the event name if over 70 chars. When logging statistics,
                    // the max property name is 100 chars so with prefix/suffix added this limit
                    // ensures that the property name is within the specified range
                    eventName = eventName.Substring(0, 70);
                }

                Name = !string.IsNullOrEmpty(eventName) ? I($"DX{eventIdText}.{eventName}") : I($"DX{eventIdText}");
            }

            public long Occurrences => m_occurrences;

            public TimeSpan Elapsed => TimeSpan.FromTicks(m_elapsedTicks);

            /// <summary>
            /// Measure a single logging operation scope
            /// </summary>
            public MeasureScope Measure(bool threadSafe)
            {
                return new MeasureScope(this, threadSafe);
            }

            public readonly struct MeasureScope : IDisposable
            {
                private readonly EventCounter m_counter;
                private readonly TimeSpan m_startTime;
                private readonly bool m_threadSafe;

                public MeasureScope(EventCounter counter, bool threadSafe)
                {
                    m_counter = counter;
                    m_startTime = TimestampUtilities.Timestamp;
                    m_threadSafe = threadSafe;
                }

                public void Dispose()
                {
                    long elapsedTicks = (TimestampUtilities.Timestamp - m_startTime).Ticks;
                    if (m_threadSafe)
                    {
                        m_counter.m_occurrences++;
                        m_counter.m_elapsedTicks += elapsedTicks;
                    }
                    else
                    {
                        Interlocked.Increment(ref m_counter.m_occurrences);
                        Interlocked.Add(ref m_counter.m_elapsedTicks, elapsedTicks);
                    }
                }
            }
        }

        /// <summary>
        /// Represents a scope when async logging is active
        /// </summary>
        private class AsyncLoggingScope : IDisposable
        {
            private readonly LoggingQueue m_loggingQueue;

            public AsyncLoggingScope(LoggingQueue loggingQueue)
            {
                m_loggingQueue = loggingQueue;
                m_loggingQueue.m_isAsyncLoggingEnabled = true;
            }

            public void Dispose()
            {
                m_loggingQueue.CompleteAsyncLoggingAndWaitForCompletion();
            }
        }
    }
}
