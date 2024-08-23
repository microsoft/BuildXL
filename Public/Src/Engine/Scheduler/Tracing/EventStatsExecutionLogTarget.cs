// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Execution log events that count the event stats for workers
    /// </summary>
    /// <remarks>
    /// This log target will count the number of times each event is handled on workers.
    /// With this target, we can validate that events sent by workers match up with what the orchestrator received.
    /// </remarks>
    public sealed class EventStatsExecutionLogTarget : ExecutionLogTargetBase
    {
        private readonly long[] m_eventCounts = new long[EnumTraits<ExecutionEventId>.MaxValue + 1];

        /// <nodoc />
        public long[] EventCounts => m_eventCounts;

        /// <summary>
        /// Handle the events from workers
        /// </summary>
        public override bool CanHandleWorkerEvents => true;

        /// <summary>
        /// Handle the events from workers
        /// </summary>
        public override IExecutionLogTarget CreateWorkerTarget(uint workerId)
        {
            return new EventStatsExecutionLogTarget();
        }

        /// <inheritdoc />
        protected override void ReportUnhandledEvent<TEventData>(TEventData data)
        {
            var eventId = data.Metadata.EventId;
            Interlocked.Increment(ref m_eventCounts[(int)eventId]);
        }
    }


}
