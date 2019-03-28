// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;

namespace Test.BuildXL.Scheduler.Utils
{
    /// <summary>
    /// Records execution log event data for enabled events
    /// </summary>
    public class ExecutionLogRecorder : ExecutionLogTargetBase
    {
        private readonly bool[] m_enabledEvents = new bool[EnumTraits<ExecutionEventId>.MaxValue + 1];

        private ConcurrentQueue<object> m_events = new ConcurrentQueue<object>();

        /// <summary>
        /// Gets events of the given type
        /// </summary>
        public IEnumerable<TEventData> GetEvents<TEventData>()
        {
            return m_events.OfType<TEventData>();
        }

        public void Clear()
        {
            m_events = new ConcurrentQueue<object>();
        }

        /// <summary>
        /// Starts recording an event
        /// </summary>
        public void StartRecording(ExecutionEventId eventId)
        {
            m_enabledEvents[(int)eventId] = true;
        }

        public override bool CanHandleEvent(ExecutionEventId eventId, long timestamp, int eventPayloadSize)
        {
            return m_enabledEvents[(int)eventId];
        }

        protected override void ReportUnhandledEvent<TEventData>(TEventData data)
        {
            m_events.Enqueue(data);
        }
    }
}
