// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using BuildXL.Tracing;
using BuildXL.Tracing.CloudBuild;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    /// Simple listener for Drop events.
    /// </summary>
    public class DropEtwListener : EventListener
    {
        private readonly ConcurrentQueue<CloudBuildEvent> m_receivedEvents;

        /// <nodoc/>
        public DropEtwListener()
            : base()
        {
            m_receivedEvents = new ConcurrentQueue<CloudBuildEvent>();
            EnableEvents(CloudBuildEventSource.TestLog, EventLevel.Verbose);
        }

        /// <summary>
        /// Is the queue of received events empty.
        /// </summary>
        public bool IsEmpty => m_receivedEvents.IsEmpty;

        /// <summary>
        /// Asserts that the queue is not empty and that the first event is a
        /// <see cref="DropOperationBaseEvent"/>; returns the first event from the queue.
        /// </summary>
        public DropOperationBaseEvent DequeueDropEvent()
        {
            Contract.Requires(!IsEmpty, "dequeue attempted on an empty event queue");

            CloudBuildEvent ev;
            var eventFound = m_receivedEvents.TryDequeue(out ev);
            Contract.Assert(eventFound, "could not dequeue cloud build event");

            var dropEvent = ev as DropOperationBaseEvent;
            Contract.Assert(dropEvent != null);

            return dropEvent;
        }

        /// <inheritdoc />
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            var ev = CloudBuildEvent.TryParse(
                eventData.EventName,
                eventData.Payload);
            Contract.Assert(ev.Succeeded, "expected to be able to parse received ETW event into CloudBuildEvent");
            m_receivedEvents.Enqueue(ev.Result);
        }
    }
}
