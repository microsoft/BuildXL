// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using System.Diagnostics.Tracing;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Utilities.Tracing;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#endif

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// EventListener for testing
    /// </summary>
    public abstract class TestEventListenerBase : BaseEventListener
    {
        /// <nodoc/>
        protected abstract void AssertTrue(bool condition, string format, params string[] args);

        /// <summary>
        /// Test listener for the currently executing test. At most one test listener should be active at a time.
        /// Note that event listeners are already effectively global due to the nature of the single EventSource object.
        /// We track test listeners in this field to catch tests that do not dispose listeners, since that causes
        /// performance problems / double-printed message confusion.
        /// </summary>
        private static TestEventListenerBase s_currentTestListener = null;

        /// <summary>
        /// Messages written to the log so far.
        /// </summary>
        /// <remarks>This is a list with a lock to ensure the order of the messages. This could be important for some observers</remarks>
        private readonly List<string> m_logMessages = new List<string>();

        /// <summary>
        /// Lock on the messages
        /// </summary>
        /// <remarks>This is a list with a lock to ensure the order of the messages. This could be important for some observers</remarks>
        private readonly object m_logMessagesLock = new object();

        /// <summary>
        /// Counts how many instances of different events have occurred.
        /// </summary>
        private readonly ConcurrentDictionary<int, int> m_eventCounter = new ConcurrentDictionary<int, int>();

        /// <summary>
        /// Counts how many instances of different events have occurred, with event counters aggregated by path key.
        /// This is used for events like <see cref="EventId.PipProcessDisallowedFileAccess"/> by which the particular counts
        /// are not easily predictable, but the paths themselves are.
        /// </summary>
        private readonly ConcurrentDictionary<int, Dictionary<string, int>> m_eventsByPathCounter = new ConcurrentDictionary<int, Dictionary<string, int>>();

        /// <summary>
        /// Name of the owning test. Used for diagnostics when these listeners leak (not disposed after a test execution).
        /// </summary>
        private readonly string m_owningTestFullyQualifiedName;

        /// <summary>
        /// Number of errors
        /// </summary>
        public int ErrorCount;

        /// <summary>
        /// Number of warnings
        /// </summary>
        public int WarningCount;

        private readonly DateTime m_baseTime = DateTime.UtcNow;

        private List<TestEventListenerBase> m_childListeners = new List<TestEventListenerBase>();

        private readonly Action<string> m_logAction;

        /// <summary>
        /// Creates an instance attached to a particular named test.
        /// </summary>
        /// <param name="eventSource">
        /// The event source to listen to.
        /// </param>
        /// <param name="fullyQualifiedTestName">
        /// Name of the owning test. Used for diagnostics when these listeners leak (not disposed after a test execution).
        /// </param>
        /// <param name="captureAllDiagnosticMessages">
        /// If true, all messages tagged with Diagnostics are captured (rather than needing to be enabled per-task).
        /// </param>
        /// <param name="logAction">
        /// Action to perform when logging a string. This allows test frameworks to hook into their own logging.
        /// Writes to the console if unspecified
        /// </param> 
        protected TestEventListenerBase(Events eventSource, string fullyQualifiedTestName, bool captureAllDiagnosticMessages = true, Action<string> logAction = null)
            : base(eventSource, null, EventLevel.Verbose, captureAllDiagnosticMessages: captureAllDiagnosticMessages, listenDiagnosticMessages: true)
        {
            Contract.Requires(eventSource != null);
            Contract.Requires(!string.IsNullOrEmpty(fullyQualifiedTestName));

            m_owningTestFullyQualifiedName = fullyQualifiedTestName;
            m_logAction = logAction;

            TestEventListenerBase existingListener = Interlocked.Exchange(ref s_currentTestListener, this);
            if (existingListener != null)
            {
                Interlocked.CompareExchange(ref s_currentTestListener, null, comparand: this);
#pragma warning disable CA2214 // Do not call overridable methods in constructors
                AssertTrue(
                    false,
                    "A TestEventListener for {0} was not disposed upon completion of the test. This can cause repeated log messages and impact test performance.",
                    existingListener.m_owningTestFullyQualifiedName);
#pragma warning restore CA2214 // Do not call overridable methods in constructors
            }
        }

        /// <nodoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public override void Dispose()
        {
            base.Dispose();
            foreach (var childListener in m_childListeners)
            {
                childListener.Dispose();
            }

            TestEventListenerBase existingListener = Interlocked.CompareExchange(ref s_currentTestListener, null, comparand: this);
            AssertTrue(existingListener == this, "TestEventListener should not be disposed after a new one has been registered (concurrent tests?)");
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Gets the logs written so far
        /// </summary>
        /// <returns>The log as written so far</returns>
        public string GetLog()
        {
            Contract.Ensures(Contract.Result<string>() != null);
            var logBuilder = new StringBuilder();
            lock (m_logMessagesLock)
            {
                foreach (string logMessage in m_logMessages)
                {
                    logBuilder.AppendLine(logMessage);
                }
            }

            return logBuilder.ToString();
        }

        private event Action<EventWrittenEventArgs> NestedLoggerHandler;

        private void Log(EventWrittenEventArgs eventData)
        {
            if (NestedLoggerHandler != null)
            {
                NestedLoggerHandler(eventData);
                return;
            }

            string s = FormattingEventListener.CreateFullMessageString(eventData, eventData.Level.ToString(), eventData.Message, m_baseTime, false);
            lock (m_logMessagesLock)
            {
                m_logMessages.Add(s);
            }

            if (m_logAction != null)
            {
                m_logAction(s);
            }
            else
            {
                Console.WriteLine(s);
            }

            // increase the use counter associated with the given event id.
            m_eventCounter.AddOrUpdate(eventData.EventId, 1, (k, v) => v + 1);

            // Increase per-path counters if applicable.
            string pathKey = TryGetPathKey((EventId)eventData.EventId, eventData.Payload);
            if (pathKey != null)
            {
                Dictionary<string, int> pathCounters = m_eventsByPathCounter.GetOrAdd(
                    eventData.EventId,
                    k => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

                lock (pathCounters)
                {
                    int existingCount;
                    pathCounters.TryGetValue(pathKey, out existingCount);
                    pathCounters[pathKey] = existingCount + 1;
                }
            }
        }

        /// <inheritdoc />
        protected override void OnCritical(EventWrittenEventArgs eventData)
        {
            Log(eventData);
            Interlocked.Increment(ref ErrorCount);
        }

        /// <inheritdoc />
        protected override void OnError(EventWrittenEventArgs eventData)
        {
            Log(eventData);
            Interlocked.Increment(ref ErrorCount);
        }

        /// <inheritdoc />
        protected override void OnWarning(EventWrittenEventArgs eventData)
        {
            Log(eventData);
            Interlocked.Increment(ref WarningCount);
        }

        /// <inheritdoc />
        protected override void OnInformational(EventWrittenEventArgs eventData)
        {
            Log(eventData);
        }

        /// <inheritdoc />
        protected override void OnVerbose(EventWrittenEventArgs eventData)
        {
            Log(eventData);
        }

        /// <inheritdoc />
        protected override void OnAlways(EventWrittenEventArgs eventData)
        {
            Log(eventData);
        }

        /// <summary>
        /// Returns a snapshot of event counts. The snapshot can be used to compare event counts subsequently.
        /// </summary>
        public EventCountsSnapshot SnapshotEventCounts()
        {
            Dictionary<int, int> snap = new Dictionary<int, int>();
            foreach (KeyValuePair<int, int> entry in m_eventCounter)
            {
                snap.Add(entry.Key, entry.Value);
            }

            return new EventCountsSnapshot(snap);
        }

        /// <summary>
        /// Gets the number of times a given event has been logged.
        /// </summary>
        public int GetEventCount(EventId eventId)
        {
            return GetEventCount((int)eventId);
        }

        /// <summary>
        /// Gets the number of times a given event has been logged.
        /// </summary>
        public int GetEventCount(int eventId)
        {
            int count;
            m_eventCounter.TryGetValue(eventId, out count);
            return count;
        }

        /// <summary>
        /// Gets the number of times a given event has been logged after the given snapshot.
        /// </summary>
        public int GetEventCountSinceSnapshot(EventId eventId, EventCountsSnapshot snapshot)
        {
            return GetEventCountSinceSnapshot((int)eventId, snapshot);
        }

        /// <summary>
        /// Gets the number of times a given event has been logged after the given snapshot.
        /// </summary>
        public int GetEventCountSinceSnapshot(int eventId, EventCountsSnapshot snapshot)
        {
            int count;
            m_eventCounter.TryGetValue(eventId, out count);
            return count - snapshot.GetEventCount(eventId);
        }

        /// <summary>
        /// Gets the number of times a given event associated to the given path has been logged.
        /// The counter for this particular event and path is then reset to zero.
        /// </summary>
        /// <remarks>
        /// This is only applicable for some events - in particular file monitoring events for which the
        /// particular number of occurrences is highly implementation dependent and hard to predict.
        /// </remarks>
        public int GetAndResetEventCountForPath(EventId eventId, string path)
        {
            Contract.Requires(eventId == EventId.PipProcessDisallowedFileAccess, "Path-keyed event assertions are not supported for this event type.");

            Dictionary<string, int> pathCounts;
            if (!m_eventsByPathCounter.TryGetValue((int)eventId, out pathCounts))
            {
                return 0;
            }

            lock (pathCounts)
            {
                int count;
                if (pathCounts.TryGetValue(path, out count))
                {
                    pathCounts[path] = 0;
                }

                return count;
            }
        }

        private string TryGetPathKey(EventId eventId, IReadOnlyCollection<object> payload)
        {
            if (eventId == EventId.PipProcessDisallowedFileAccess)
            {
                AssertTrue(payload.Count == 6, "Payload for PipProcessDisallowedFileAccess has changed. Does the ElementAt below need to be updated?");
                return (string)payload.ElementAt(5);
            }

            return null;
        }
    }

    /// <summary>
    /// Snapshot of event counts as collected by a <see cref="TestEventListenerBase"/>.
    /// </summary>
    public sealed class EventCountsSnapshot
    {
        private readonly Dictionary<int, int> m_counts;

        internal EventCountsSnapshot(Dictionary<int, int> counts)
        {
            Contract.Requires(counts != null);
            m_counts = counts;
        }

        /// <summary>
        /// Gets the number of times a given event has been logged as of this snapshot.
        /// </summary>
        public int GetEventCount(EventId eventId)
        {
            return GetEventCount((int)eventId);
        }

        /// <summary>
        /// Gets the number of times a given event has been logged as of this snapshot.
        /// </summary>
        public int GetEventCount(int eventId)
        {
            int count;
            m_counts.TryGetValue(eventId, out count);
            return count;
        }
    }
}
