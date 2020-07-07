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
    public sealed class TracerEventListener : TextWriterEventListener
    {
        /// <summary>
        /// Creates a new instance configured to output to the given writer.
        /// </summary>
        public TracerEventListener(Events eventSource, TextWriter writer, DateTime baseTime, DisabledDueToDiskWriteFailureEventHandler? onDisabledDueToDiskWriteFailure = null)
            : base(
                eventSource,
                writer,
                baseTime,
                warningMapper: null,
                level: EventLevel.Verbose,
                eventMask: new EventMask(enabledEvents: new[] { (int)LogEventId.TracerStartEvent, (int)LogEventId.TracerStopEvent, (int)LogEventId.TracerCompletedEvent, (int)LogEventId.TracerCounterEvent, (int)LogEventId.TracerSignalEvent }, disabledEvents: null),
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
#pragma warning disable CS8605
            Contract.AssertNotNull(eventData.Payload);
            if (eventData.EventId == (int)LogEventId.TracerSignalEvent)
            {
                var ticks = (long)eventData.Payload[1];
                var ts = (long)(new DateTime(ticks) - BaseTime).TotalMilliseconds;
                Output(eventData.Level, eventData, string.Format(CultureInfo.InvariantCulture, "{{\"name\": \"{0}\", \"ph\": \"i\", \"ts\": {1}000, \"s\": \"g\"}},", eventData.Payload[0], ts));
            }
            else if (eventData.EventId == (int)LogEventId.TracerStartEvent)
            {
                Output(eventData.Level, eventData, "{\"traceEvents\": [");
            }
            else if (eventData.EventId == (int)LogEventId.TracerStopEvent)
            {
                Output(eventData.Level, eventData, "{}]}");
            }
            else if (eventData.EventId == (int)LogEventId.TracerCompletedEvent)
            {
                var ticks = (long)eventData.Payload[4];
                var ts = (long)(new DateTime(ticks) - BaseTime).TotalMilliseconds;
                Output(
                    eventData.Level, 
                    eventData, 
                    string.Format(CultureInfo.InvariantCulture, "{{\"name\": \"{0}\", \"cat\": \"{1}\", \"ph\": \"X\", \"pid\": \"{2}\", \"tid\": {3}, \"ts\": {4}000, \"dur\": {5}000, \"args\": {{\"desc\": \"{6}\"}}}},",
                        eventData.Payload[0],
                        eventData.Payload[1],
                        eventData.Payload[2],
                        eventData.Payload[3],
                        ts,
                        eventData.Payload[5],
                        eventData.Payload[6]));
            }
            else if (eventData.EventId == (int)LogEventId.TracerCounterEvent)
            {
                var ticks = (long)eventData.Payload[2];
                var ts = (long)(new DateTime(ticks) - BaseTime).TotalMilliseconds;

                if (ts < 0)
                {
                    // There might be some worker events that have occurred before master is started.
                    // As those will be sent when the master and worker are attached, it is safe to ignore them here.
                    return;
                }
                
                Output(
                    eventData.Level,
                    eventData,
                    string.Format(CultureInfo.InvariantCulture, "{{\"name\": \"{0}\", \"ph\": \"C\", \"pid\": \"{1} - Counters\", \"ts\": {2}000, \"args\": {{\"%\": \"{3}\"}}}},",
                        eventData.Payload[0],
                        eventData.Payload[1],
                        ts,
                        eventData.Payload[3]));
            }
#pragma warning restore CS8605
        }
    }
}
