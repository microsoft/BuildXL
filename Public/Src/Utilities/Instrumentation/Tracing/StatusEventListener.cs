// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public StatusEventListener(Events eventSource, TextWriter writer, DateTime baseTime, DisabledDueToDiskWriteFailureEventHandler? onDisabledDueToDiskWriteFailure = null)
            : base(
                eventSource,
                writer,
                baseTime,
                warningMapper: null,
                level: EventLevel.Verbose,
                eventMask: new EventMask(enabledEvents: new[] { (int)LogEventId.Status, (int)LogEventId.StatusHeader }, disabledEvents: null),
                onDisabledDueToDiskWriteFailure: onDisabledDueToDiskWriteFailure,
                listenDiagnosticMessages: true)
        {
            Contract.RequiresNotNull(eventSource);
            Contract.RequiresNotNull(writer);
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
        protected override void Write(EventWrittenEventArgs eventData, EventLevel level, string? message = null, bool suppressEvent = false)
        {
            if (eventData.EventId == (int)LogEventId.StatusHeader)
            {
                Contract.AssertNotNull(eventData.Payload);
                Output(eventData.Level, eventData, string.Format(CultureInfo.InvariantCulture, "{0},{1}", TimeHeaderText, eventData.Payload[0]));
            }
            else if (eventData.EventId == (int)LogEventId.Status)
            {
                Contract.AssertNotNull(eventData.Payload);

                var t = DateTime.UtcNow - BaseTime;
                var time = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}:{1:d02}:{2:d02}",
                        (t.Days * 24) + t.Hours,
                        t.Minutes,
                        t.Seconds);
                Output(eventData.Level, eventData, string.Format(CultureInfo.InvariantCulture, "{0},{1}", time.PadLeft(TimeHeaderText.Length), eventData.Payload[0]));
            }
        }
    }
}
