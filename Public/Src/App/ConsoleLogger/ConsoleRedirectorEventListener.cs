// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL
{
    /// <summary>
    /// Listens to an event source and selectively send events to the console
    /// </summary>
    public sealed class ConsoleRedirectorEventListener : FormattingEventListener
    {
        private readonly HashSet<int> m_eventIdsToMap;
        private readonly LoggingContext m_loggingContext;
#if !NETCOREAPP
        private static readonly char[] s_delimiter = new[] { '\n' };
#endif
        /// <nodoc />
        public ConsoleRedirectorEventListener(
            Events eventSource,
            DateTime baseTime,
            IEnumerable<int> eventsToRedirect,
            LoggingContext loggingContext,
            WarningMapper warningMapper)
            : base(eventSource, baseTime, warningMapper: warningMapper, level: EventLevel.Verbose, captureAllDiagnosticMessages: false, timeDisplay: TimeDisplay.Seconds)
        {
            Contract.Requires(eventsToRedirect != null);
            m_eventIdsToMap = new HashSet<int>(eventsToRedirect);
            m_loggingContext = loggingContext;
        }

        /// <inheritdoc />
        protected override void Output(EventLevel level, EventWrittenEventArgs eventData, string text, bool doNotTranslatePaths = false)
        {
            var eventId = eventData.EventId;

            var forwarded = eventId == (int)SharedLogEventId.DistributionWorkerForwardedError 
                || eventId == (int)SharedLogEventId.DistributionWorkerForwardedWarning 
                || eventId == (int)SharedLogEventId.DistributionWorkerForwardedEvent;

            if (forwarded)
            {
                // If this is a forwarded event, we want to make the decision to log
                // or not based on the actual event that was logged in the worker.
                eventId = (int)eventData.Payload[1];
            }

            if (!m_eventIdsToMap.Contains(eventId))
            {
                return;
            }

            if (forwarded)
            {
                // When logging to the console, we hide the fact that we are forwarding this event from the worker
                // The text is of the form "Worker X forwarded (error/warning/event):\n{payload logged on the worker}"
                // so we split on the newline to get the actual text, and prepend the timestamp.
                // We enforce this format via DevOpsListenerTests.DependOnASpecificMessageFormatForForwardedEvents
#if NETCOREAPP
                var forwardedText = text.Split('\n', 2)[1];
#else
                var forwardedText = text.Split(s_delimiter , 2)[1];
#endif
                text = $"{TimeSpanToString(TimeDisplay, DateTime.UtcNow - BaseTime)} {forwardedText}";
            }

            // If the event should be redirected, log it via this special log message
            ConsoleRedirector.Tracing.Logger.Log.LogToConsole(m_loggingContext, text);
        }
    }
}
