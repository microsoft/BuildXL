// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Threading;
using BuildXL.Utilities.Collections;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Captures ETW data pumped through any instance derived from <see cref="Events" /> and counts the number of errors and
    /// warnings reported.
    /// </summary>
    public sealed class TrackingEventListener : BaseEventListener
    {
        private const int MaxEventIdExclusive = ushort.MaxValue;

        private int m_numAlways;
        private int m_numCriticals;
        private int m_numInformationals;
        private int m_numVerbose;
        private int m_numWarnings;
        private int m_numEventSourceInternalWarnings;

        private int m_numInternalErrors;
        private int m_numInfrastructureErrors;
        private int m_numUserErrors;

        private readonly BigBuffer<int> m_eventCounts = new BigBuffer<int>(entriesPerBufferBitWidth: 14);

        /// <summary>
        /// Initializes an instance.
        /// </summary>
        /// <param name="eventSource">
        /// The event source to listen to.
        /// </param>
        /// <param name="warningMapper">
        /// An optional delegate that is used to map warnings into errors or to suppress warnings.
        /// </param>
        public TrackingEventListener(Events eventSource, WarningMapper warningMapper = null)
            : base(
                eventSource,
                warningMapper,
                EventLevel.Verbose)
        {
            Contract.Requires(eventSource != null);
            m_eventCounts.Initialize(MaxEventIdExclusive);
        }

        /// <inheritdoc/>
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            // Count all events
            if (m_eventCounts.Capacity > eventData.EventId && eventData.EventId >= 0)
            {
                var bufferPointer = m_eventCounts.GetBufferPointer(eventData.EventId);
                Interlocked.Increment(ref bufferPointer.Buffer[bufferPointer.Index]);
            }

            // then go through the event mapping and filtering logic defined in BaseEventListener
            base.OnEventWritten(eventData);
        }

        /// <summary>
        /// Gets the each event that was called and the count of times it was called
        /// </summary>
        public IEnumerable<KeyValuePair<int, int>> CountsPerEvent
        {
            get
            {
                var accessor = m_eventCounts.GetAccessor();
                for (int i = 0; i < MaxEventIdExclusive; i++)
                {
                    var eventCount = accessor[i];
                    if (eventCount != 0)
                    {
                        yield return new KeyValuePair<int, int>(i, eventCount);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the number of events logged for eventId.
        /// </summary>
        /// <param name="eventId">The eventId which count to return.</param>
        /// <returns>The number of tome the iventId was raised.</returns>
        /// <remarks>If the eventId is not in the range of available eventIds, the return value is 0.</remarks>
        public int CountsPerEventId(EventId eventId)
        {
            var accessor = m_eventCounts.GetAccessor();
            var eventIdInt = (int)eventId;

            if (eventId >= 0 && eventIdInt < MaxEventIdExclusive)
            {
                return accessor[eventIdInt];
            }

            return 0;
        }

        /// <summary>
        /// Gets the number of critical events written.
        /// </summary>
        public int CriticalCount => Volatile.Read(ref m_numCriticals);

        /// <summary>
        /// Gets the number of warning events written.
        /// </summary>
        public int WarningCount => Volatile.Read(ref m_numWarnings);

        /// <summary>
        /// Gets the number of meta-warnings written (warnings related to the act of writing other events).
        /// </summary>
        public int EventSourceInternalWarningCount => Volatile.Read(ref m_numEventSourceInternalWarnings);

        /// <summary>
        /// Gets the number of error events written.
        /// </summary>
        public int TotalErrorCount => Volatile.Read(ref m_numInternalErrors) + Volatile.Read(ref m_numInfrastructureErrors) + Volatile.Read(ref m_numUserErrors);

        /// <summary>
        /// Gets the number of informational events written.
        /// </summary>
        public int InformationalCount => Volatile.Read(ref m_numInformationals);

        /// <summary>
        /// Gets the number of verbose events written.
        /// </summary>
        public int VerboseCount => Volatile.Read(ref m_numVerbose);

        /// <summary>
        /// Gets the number of 'always' events written.
        /// </summary>
        public int AlwaysCount => Volatile.Read(ref m_numAlways);

        /// <summary>
        /// Gets the number of user errors
        /// </summary>
        public int UserErrorCount => Volatile.Read(ref m_numUserErrors);

        /// <summary>
        /// The name of the first user error encountered
        /// </summary>
        public string FirstUserErrorName { get; private set; }

        /// <summary>
        /// The name of the last user error encountered
        /// </summary>
        public string LastUserErrorName { get; private set; }

        /// <summary>
        /// Gets the number internal errors
        /// </summary>
        public int InternalErrorCount => Volatile.Read(ref m_numInternalErrors);

        /// <summary>
        /// The name of the first internal error encountered
        /// </summary>
        public string FirstInternalErrorName { get; private set; }

        /// <summary>
        /// The name of the last internal error encountered
        /// </summary>
        public string LastInternalErrorName { get; private set; }

        /// <summary>
        /// Gets the number of infrastructure errors
        /// </summary>
        public int InfrastructureErrorCount => Volatile.Read(ref m_numInfrastructureErrors);

        /// <summary>
        /// The name of the first Infrastructure error encountered
        /// </summary>
        public string FirstInfrastructureErrorName { get; private set; }

        /// <summary>
        /// The name of the last Infrastructure error encountered
        /// </summary>
        public string LastInfrastructureErrorName { get; private set; }

        /// <summary>
        /// Gets whether any errors were written.
        /// </summary>
        public bool HasFailures => TotalErrorCount > 0 || CriticalCount > 0;

        /// <summary>
        /// Gets whether any errors or warnings were written.
        /// </summary>
        /// <remarks>
        /// This is useful in unit tests to ensure no error was logged.
        /// </remarks>
        public bool HasFailuresOrWarnings => TotalErrorCount > 0 || CriticalCount > 0 || WarningCount > 0;

        /// <inheritdoc />
        protected override void OnCritical(EventWrittenEventArgs eventData)
        {
            Contract.Assume(eventData.Level == EventLevel.Critical);
            long keywords = (long)eventData.Keywords;
            string eventName = eventData.EventName;

            BucketError(keywords, eventName);
            Interlocked.Increment(ref m_numCriticals);
        }

        /// <inheritdoc />
        protected override void OnWarning(EventWrittenEventArgs eventData)
        {
            Contract.Assume(eventData.Level == EventLevel.Warning);
            Interlocked.Increment(ref m_numWarnings);
        }

        /// <inheritdoc />
        protected override void OnEventSourceInternalWarning(EventWrittenEventArgs eventData)
        {
            Interlocked.Increment(ref m_numEventSourceInternalWarnings);
        }

        /// <inheritdoc />
        protected override void OnError(EventWrittenEventArgs eventData)
        {
            var level = eventData.Level;
            if (level == EventLevel.Error)
            {
                long keywords = (long)eventData.Keywords;
                string eventName = eventData.EventName;

                // Errors replayed from workers should respect their original event name and keywords
                if (eventData.EventId == (int)EventId.DistributionWorkerForwardedError)
                {
                    eventName = (string)eventData.Payload[2];
                    keywords = (long)eventData.Payload[3];
                }

                BucketError(keywords, eventName);
            }
            else
            {
                // OnError() called for an EventLevel other than error?!? How can this be? It is possible for the
                // WarningMapper to upconvert a lower priority event to an error
                Contract.Assert((level == EventLevel.Warning) || (level == EventLevel.Informational) || (level == EventLevel.Verbose));

                // The configuration promoted a warning to an error. That's a user error
                if (Interlocked.Increment(ref m_numUserErrors) == 1)
                {
                    FirstUserErrorName = eventData.EventName;
                }
            }
        }

        private void BucketError(long keywords, string eventName)
        {
            if ((keywords & (long)Events.Keywords.UserError) > 0)
            {
                if (Interlocked.Increment(ref m_numUserErrors) == 1)
                {
                    FirstUserErrorName = eventName;
                }

                LastUserErrorName = eventName;
            }
            else if ((keywords & (long)Events.Keywords.InfrastructureError) > 0)
            {
                if (Interlocked.Increment(ref m_numInfrastructureErrors) == 1)
                {
                    FirstInfrastructureErrorName = eventName;
                }

                LastInfrastructureErrorName = eventName;
            }
            else
            {
                if (Interlocked.Increment(ref m_numInternalErrors) == 1)
                {
                    FirstInternalErrorName = eventName;
                }

                LastInternalErrorName = eventName;
            }
        }

        /// <inheritdoc />
        protected override void OnInformational(EventWrittenEventArgs eventData)
        {
            Contract.Assume(eventData.Level == EventLevel.Informational);
            Interlocked.Increment(ref m_numInformationals);
        }

        /// <inheritdoc />
        protected override void OnVerbose(EventWrittenEventArgs eventData)
        {
            Contract.Assume(eventData.Level == EventLevel.Verbose);
            Interlocked.Increment(ref m_numVerbose);
        }

        /// <inheritdoc />
        protected override void OnAlways(EventWrittenEventArgs eventData)
        {
            Contract.Assume(eventData.Level == EventLevel.LogAlways);
            Interlocked.Increment(ref m_numAlways);
        }

        /// <summary>
        /// Returns a dictionary of the number of times each event was encountered.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, int> ToEventCountDictionary()
        {
            Dictionary<string, int> d = new Dictionary<string, int>();
            foreach (var entry in CountsPerEvent)
            {
                d.Add(entry.Key.ToString(CultureInfo.InvariantCulture), entry.Value);
            }

            return d;
        }
    }
}
