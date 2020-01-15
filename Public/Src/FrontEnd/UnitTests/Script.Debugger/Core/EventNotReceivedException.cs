// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Test.DScript.Debugger
{
    public enum EventNotReceivedReason { DebuggerFinished, TimedOut }

    public class EventNotReceivedException : DebuggerTestException
    {
        public Type EventType { get; }

        public EventNotReceivedReason Reason { get; }

        private EventNotReceivedException(Type eventType, EventNotReceivedReason reason, string message)
            : base(message)
        {
            EventType = eventType;
            Reason = reason;
        }

        public static EventNotReceivedException Timeout(Type eventType, EventNotReceivedReason reason)
        {
            return new EventNotReceivedException(eventType, reason, $"Debugger didn't receive {eventType.Name} within allotted time.");
        }

        public static EventNotReceivedException Stopped(Type eventType, EventNotReceivedReason reason)
        {
            return new EventNotReceivedException(eventType, reason, $"Debugger didn't receive {eventType.Name} within allotted time but already got a stop event.");
        }
    }
}
