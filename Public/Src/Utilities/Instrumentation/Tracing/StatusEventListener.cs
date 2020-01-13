// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using System;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Tracing
{
    /// <nodoc />
    public sealed class StatusEventListener : TextWriterEventListener
    {
        private const string TimeHeaderText = "Time      ";

        /// <summary>
        /// Creates a new instance configured to output to the given writer.
        /// </summary>
        public StatusEventListener(Events eventSource, TextWriter writer, DateTime baseTime, DisabledDueToDiskWriteFailureEventHandler onDisabledDueToDiskWriteFailure = null)
            : base(
                eventSource,
                writer,
                baseTime,
                warningMapper: null,
                level: EventLevel.Verbose,
                eventMask: new EventMask(enabledEvents: new[] { (int)EventId.Status, (int)EventId.StatusHeader }, disabledEvents: null),
                onDisabledDueToDiskWriteFailure: onDisabledDueToDiskWriteFailure,
                listenDiagnosticMessages: true)
        {
            Contract.Requires(eventSource != null);
            Contract.Requires(writer != null);
        }

        /// <summary>
        /// Registers an additional event source (a generated ETW event source)
        /// </summary>
        public override void RegisterEventSource(EventSource eventSource)
        {
            // StatisticsEventListener only needs to listen to the diagnostics events from Tracing.ETWLogger
            if (eventSource == BuildXL.Tracing.ETWLogger.Log)
            {
                base.RegisterEventSource(eventSource);
            }
        }

        /// <inheritdoc/>
        protected override void Write(EventWrittenEventArgs eventData, EventLevel level, string message = null, bool suppressEvent = false)
        {
            if (eventData.EventId == (int)EventId.StatusHeader)
            {
                Output(eventData.Level, eventData.EventId, eventData.GetEventName(), eventData.Keywords, string.Format(CultureInfo.InvariantCulture, "{0},{1}", TimeHeaderText, eventData.Payload[0]));
            }
            else if (eventData.EventId == (int)EventId.Status)
            {
                var time = TimeSpanToString(TimeDisplay.Seconds, DateTime.UtcNow - BaseTime);
                Output(eventData.Level, eventData.EventId, eventData.GetEventName(), eventData.Keywords, string.Format(CultureInfo.InvariantCulture, "{0},{1}", time.PadLeft(TimeHeaderText.Length), eventData.Payload[0]));
            }
        }
    }
}
