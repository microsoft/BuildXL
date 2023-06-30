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
            if (!m_eventIdsToMap.Contains(eventData.EventId))
            {
                return;
            }

            // If the event should be redirected, log it via this special log message
            ConsoleRedirector.Tracing.Logger.Log.LogToConsole(m_loggingContext, text);
        }
    }
}
