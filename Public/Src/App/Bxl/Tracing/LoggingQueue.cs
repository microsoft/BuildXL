// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Threading;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Tracing
{
    /// <summary>
    /// Asynchronous logging queue which queues log messages and processes them on a dedicated logging thread.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    internal class LoggingQueue : ILoggingQueue
    {
        /// <summary>
        /// Indicates whether async logging is currently enabled
        /// </summary>
        private bool m_isAsyncLoggingEnabled = false;

        /// <summary>
        /// The timestamp when async logging was initiated
        /// </summary>
        private TimeSpan m_completeAsyncLoggingStart;

        /// <summary>
        /// Logging context for logging statistics about async logging upon completion
        /// </summary>
        private LoggingContext m_loggingContext;

        /// <summary>
        /// Counters for events. Only counters for logged events are actually created
        /// </summary>
        private readonly EventCounter[] m_counters = new EventCounter[ushort.MaxValue];

        /// <summary>
        /// Queue of log actions and associated counters
        /// </summary>
        private readonly BlockingCollection<(EventCounter, Action)> m_logActionQueue = new BlockingCollection<(EventCounter, Action)>();

        /// <summary>
        /// Lock used when completing async logging so that all async log operations are guaranteed to
        /// be flushed to log queue. Subsequent operations will be logged synchronously
        /// </summary>
        private readonly ReadWriteLock m_lock = ReadWriteLock.Create();

        /// <summary>
        /// Enqueues a log action for async logging
        /// </summary>
        public void EnqueueLogAction(int eventId, Action logAction, string eventName)
        {
            Contract.Requires(logAction != null);

            var eventCounter = GetEventCounter(eventId, eventName);

            // This lock is only for ensuring that the completion of async logging
            // does not occur concurrently with addition to the queue. Hence, a shared (read)
            // lock is used here since the queue is concurrent-safe so the lock is not needed
            // for managing queue concurrency. The exclusive (write) lock is used by the 
            // CompleteAsyncLogging method.
            using (m_lock.AcquireReadLock())
            {
                if (m_isAsyncLoggingEnabled)
                {
                    m_logActionQueue.Add((eventCounter, logAction));
                }
                else
                {
                    MeasuredLog(eventCounter, logAction);
                }
            }
        }

        /// <summary>
        /// Logs the event using the given <paramref name="logAction"/> and measures it duration
        /// </summary>
        private static void MeasuredLog(EventCounter eventCounter, Action logAction)
        {
            using (eventCounter.Measure())
            {
                logAction();
            }
        }

        /// <summary>
        /// Gets or creates the counter for a specific event
        /// </summary>
        private EventCounter GetEventCounter(int eventId, string eventName)
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

        /// <summary>
        /// Activates async logging which queues log operations to dedicated thread
        /// </summary>
        public IDisposable EnterAsyncLoggingScope(LoggingContext loggingContext)
        {
            Contract.Requires(!m_isAsyncLoggingEnabled, "EnterAsyncLoggingScope must only be called once");
            m_loggingContext = loggingContext;
            return new AsyncLoggingScope(this);
        }

        /// <summary>
        /// Completes async logging
        /// </summary>
        private void CompleteAsyncLogging()
        {
            m_completeAsyncLoggingStart = TimestampUtilities.Timestamp;

            // See EnqueueLogAction for details on the semantics of this lock
            using (m_lock.AcquireWriteLock())
            {
                m_isAsyncLoggingEnabled = false;
                m_logActionQueue.CompleteAdding();
            }
        }

        /// <summary>
        /// Called by dedicated logging thread to drain the logging queue
        /// </summary>
        private void DrainLoggingQueue()
        {
            foreach (var item in m_logActionQueue.GetConsumingEnumerable())
            {
                MeasuredLog(item.Item1, item.Item2);
            }

            // Compute statistics about async logging
            Dictionary<string, long> statistics = new Dictionary<string, long>();

            var asyncLoggingOverhang = TimestampUtilities.Timestamp - m_completeAsyncLoggingStart;
            statistics["AsyncLoggingOverhangMs"] = (long)asyncLoggingOverhang.TotalMilliseconds;
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

            BuildXL.Tracing.Logger.Log.LoggerStatistics(m_loggingContext, statistics);
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

            public EventCounter(int eventId, string eventName)
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
            public MeasureScope Measure()
            {
                return new MeasureScope(this);
            }

            public readonly struct MeasureScope : IDisposable
            {
                private readonly EventCounter m_counter;
                private readonly TimeSpan m_startTime;

                public MeasureScope(EventCounter counter)
                {
                    m_counter = counter;
                    m_startTime = TimestampUtilities.Timestamp;
                }

                public void Dispose()
                {
                    Interlocked.Increment(ref m_counter.m_occurrences);
                    Interlocked.Add(ref m_counter.m_elapsedTicks, (TimestampUtilities.Timestamp - m_startTime).Ticks);
                }
            }
        }

        /// <summary>
        /// Represents a scope when async logging is active
        /// </summary>
        private class AsyncLoggingScope : IDisposable
        {
            private readonly LoggingQueue m_loggingQueue;
            private readonly Thread m_thread;

            public AsyncLoggingScope(LoggingQueue loggingQueue)
            {
                m_loggingQueue = loggingQueue;
                m_loggingQueue.m_isAsyncLoggingEnabled = true;
                m_thread = new Thread(m_loggingQueue.DrainLoggingQueue)
                {
                    Name = "Async Logging Thread"
                };

                m_thread.Start();
            }

            public void Dispose()
            {
                m_loggingQueue.CompleteAsyncLogging();
                m_thread.Join();
            }
        }
    }
}
