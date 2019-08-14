// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using BuildXL.Cache.ImplementationSupport;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// Listener to verify that no ETW traces were malformed, or that an activity was not Disposed as the close operation
    /// </summary>
    internal class TestEventListener : EventListener
    {
        /// <summary>
        /// True if an ETW message was malformed, or if Dispose was called instead of Stop.
        /// </summary>
        public bool HasSeenErrors = false;

        public readonly ConcurrentBag<StackTrace> FailedEventStackTraces = new ConcurrentBag<StackTrace>();

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName.Equals("EventSourceMessage", StringComparison.OrdinalIgnoreCase) ||
                eventData.EventName.Equals(CacheActivity.DisposedEventName, StringComparison.OrdinalIgnoreCase))
            {
                FailedEventStackTraces.Add(new StackTrace(true));

                HasSeenErrors = true;
            }

            base.OnEventWritten(eventData);
        }
    }
}
