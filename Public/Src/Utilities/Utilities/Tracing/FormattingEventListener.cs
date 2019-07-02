// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Controls the production of a time display for each line of a listener's output.
    /// </summary>
    public enum TimeDisplay
    {
        /// <summary>
        /// No time is produced.
        /// </summary>
        None,

        /// <summary>
        /// Display time to second accuracy.
        /// </summary>
        Seconds,

        /// <summary>
        /// Display time to millisecond accuracy.
        /// </summary>
        Milliseconds,
    }

    /// <summary>
    /// Captures ETW data pumped through any instance derived from <see cref="Events" /> and provides primitives for text formatting the captured data.
    /// </summary>
    public abstract class FormattingEventListener : BaseEventListener
    {
        /// <summary>
        /// A special marker that indicates the beginning of the custom pip description in the pip description string.
        /// Pip.GetDescription() uses this marker when creating the description string.
        /// </summary>
        public const string CustomPipDescriptionMarker = " || ";

        /// <summary>
        /// The UTC time representing time 0 for this listener
        /// </summary>
        protected readonly DateTime BaseTime;

        /// <summary>
        /// The time format that should be used
        /// </summary>
        protected readonly TimeDisplay TimeDisplay;

        private IEtwOnlyTextLogger m_etwLogger;

        /// <summary>
        /// Indicates whether we should attempt to shorten pip description in errors and warnings to (SemiStableHash, CustomerSuppliedPipDescription)
        /// </summary>
        protected readonly bool UseCustomPipDescription;

        /// <summary>
        /// Creates a new instance.
        /// </summary>
        /// <param name="eventSource">
        /// The event source to listen to.
        /// </param>
        /// <param name="baseTime">
        /// The UTC time representing time 0 for this listener.
        /// </param>
        /// <param name="warningMapper">
        /// An optional delegate that is used to map warnings into errors or to suppress warnings.
        /// </param>
        /// <param name="level">
        /// The base level of data to be sent to the listener.
        /// </param>
        /// <param name="captureAllDiagnosticMessages">
        /// If true, all messages tagged with <see cref="Keywords.Diagnostics" /> are captured.
        /// </param>
        /// <param name="timeDisplay">
        /// The kind of time prefix to apply to each chunk of output.
        /// </param>
        /// <param name="eventMask">
        /// If specified, an EventMask that allows selectively enabling or disabling events
        /// </param>
        /// <param name="onDisabledDueToDiskWriteFailure">
        /// If specified, called if the listener encounters a disk-write failure such as an out of space condition.
        /// Otherwise, such conditions will throw an exception.
        /// </param>
        /// <param name="listenDiagnosticMessages">
        /// If true, all messages tagged with <see cref="Keywords.Diagnostics" /> at or above <paramref name="level"/> are enabled but not captured unless diagnostics are enabled per-task.
        /// This is useful for StatisticsEventListener, where you need to listen diagnostics messages but capture only ones tagged with CommonInfrastructure task.
        /// </param>
        /// <param name="useCustomPipDescription">
        /// If true, pip description string will be changed to (SemiStableHash, CustomerSuppliedPipDescription).
        /// If true but no custom description available, no changes will be made.
        /// </param>
        protected FormattingEventListener(
            Events eventSource,
            DateTime baseTime,
            WarningMapper warningMapper = null,
            EventLevel level = EventLevel.Verbose,
            bool captureAllDiagnosticMessages = false,
            TimeDisplay timeDisplay = TimeDisplay.None,
            EventMask eventMask = null,
            DisabledDueToDiskWriteFailureEventHandler onDisabledDueToDiskWriteFailure = null,
            bool listenDiagnosticMessages = false,
            bool useCustomPipDescription = false)
            : base(eventSource, warningMapper, level, captureAllDiagnosticMessages, eventMask, onDisabledDueToDiskWriteFailure, listenDiagnosticMessages)
        {
            Contract.Requires(eventSource != null);

            BaseTime = baseTime;
            TimeDisplay = timeDisplay;
            UseCustomPipDescription = useCustomPipDescription;
        }

        /// <summary>
        /// Performs the final output activity for the listener.
        /// </summary>
        protected abstract void Output(EventLevel level, int id, string eventName, EventKeywords eventKeywords, string text,bool doNotTranslatePaths = false);

        /// <summary>
        /// Enables sending log output to ETW via the provider logger
        /// </summary>
        public void EnableEtwOutputLogging(IEtwOnlyTextLogger etwLogger)
        {
            m_etwLogger = etwLogger;
        }

        /// <summary>
        /// Formats the event data into a string and dispatches it to the <see cref="Output"/> method.
        /// </summary>
        /// <param name="eventData">The event. Note the level cannot be trusted</param>
        /// <param name="level">A separate event level is taken because warnings may be promoted to errors based on
        /// configuration.</param>
        /// <param name="message">An override for the event's message</param>
        /// <param name="suppressEvent">Whether an event should be suppressed</param>
        protected virtual void Write(EventWrittenEventArgs eventData, EventLevel level, string message = null, bool suppressEvent = false)
        {
            Contract.Requires(eventData != null);

            string label;
            switch (level)
            {
                case EventLevel.Critical:
                    label = "critical";
                    break;
                case EventLevel.Error:
                    label = "error";
                    break;
                case EventLevel.Warning:
                    label = suppressEvent ? "NoWarn" : "warning";
                    break;
                case EventLevel.Informational:
                    label = "info";
                    break;
                case EventLevel.Verbose:
                    label = "verbose";
                    break;
                case EventLevel.LogAlways:
                    label = "message";
                    break;
                default:
                    throw Contract.AssertFailure("Unknown level:" + level);
            }

            if (message == null)
            {
                // if no local override, use the default
                message = eventData.Message;
            }

            if (message != null)
            {
                string full = CreateFullMessageString(eventData, label, message, BaseTime, UseCustomPipDescription, TimeDisplay);

                m_etwLogger?.TextLogEtwOnly(
                    eventNumber: eventData.EventId,
                    eventLabel: label,
                    message: full);

                // Don't translate paths in the DominoInvocation event since that contains bxl.exe's command line. It
                // is useful to see exactly how BuildXL was invoked since some of those options control the translation.
                Output(level, eventData.EventId, eventData.EventName, eventData.Keywords, full, doNotTranslatePaths: eventData.EventId == (int)EventId.DominoInvocation);
            }
        }

        /// <nodoc/>
        public static string TimeSpanToString(TimeDisplay timeDisplay, TimeSpan t)
        {
            if (timeDisplay == TimeDisplay.Seconds)
            {
                string text;
                if (t.Days > 0 || t.Hours > 0)
                {
                    text = string.Format(
                        CultureInfo.InvariantCulture,
                        "[{0}:{1:d02}:{2:d02}]",
                        (t.Days * 24) + t.Hours,
                        t.Minutes,
                        t.Seconds);
                }
                else
                {
                    text = string.Format(
                        CultureInfo.InvariantCulture,
                        "[{0}:{1:d02}]",
                        t.Minutes,
                        t.Seconds);
                }

                return text;
            }
            else
            {
                string text;
                if (t.Days > 0 || t.Hours > 0)
                {
                    text = string.Format(
                        CultureInfo.InvariantCulture,
                        "[{0}:{1:d02}:{2:d02}.{3:d03}]",
                        (t.Days * 24) + t.Hours,
                        t.Minutes,
                        t.Seconds,
                        t.Milliseconds);
                }
                else
                {
                    text = string.Format(
                        CultureInfo.InvariantCulture,
                        "[{0}:{1:d02}.{2:d03}]",
                        t.Minutes,
                        t.Seconds,
                        t.Milliseconds);
                }

                return text;
            }
        }

        /// <summary>
        /// Creates the full message including error code for a given eventData object.
        /// </summary>
        public static string CreateFullMessageString(EventWrittenEventArgs eventData, string label, string message, DateTime baseTime, bool useCustomPipDescription, TimeDisplay timeDisplay = TimeDisplay.Seconds)
        {
            Contract.Requires(eventData != null);
            Contract.Requires(label != null);
            Contract.Requires(message != null);

            // Note that we cannot assume that Payload or other fields are actually populated.
            // In particular, an event-write failure goes through EventSource.WriteStringToAllListeners,
            // which synthesizes minimal args - event ID 0, a message, and possibly invalid metadata.
            object[] args = eventData.Payload == null ? CollectionUtilities.EmptyArray<object>() : eventData.Payload.ToArray();
            string full;

            // see if this event provides provenance info
            if (message.StartsWith(EventConstants.ProvenancePrefix, StringComparison.Ordinal))
            {
                Contract.Assume(args.Length >= 3, "Provenance prefix contains 3 formatting tokens.");

                // this is formatted with local culture
                string body = string.Format(CultureInfo.CurrentCulture, message.Substring(EventConstants.ProvenancePrefix.Length), args);

                // this is formatted with the invariant culture since it is parsed by VS
                full = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}({1},{2}): {3} DX{4:D4}: {5}",

                    // file
                    args[0],

                    // line
                    args[1],

                    // column
                    args[2],

                    // error/warning/info/etc
                    label,

                    // error/warning #
                    eventData.EventId,

                    // error/warning text
                    body);
            }
            else if ((eventData.Keywords & Keywords.Progress) != 0 || message.StartsWith(EventConstants.PhasePrefix, StringComparison.Ordinal))
            {
                // Phases get printed without any DX code
                string body = string.Format(CultureInfo.CurrentCulture, message, args);

                // this is formatted with the invariant culture since it is parsed by VS
                full = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1}",
                    TimeSpanToString(timeDisplay, DateTime.UtcNow - baseTime),

                    // error/warning text
                    body);
            }
            else
            {
                // a plain message with no provenance info

                // this is formatted with local culture
                string body = string.Format(CultureInfo.CurrentCulture, message, args);

                if (timeDisplay != TimeDisplay.None)
                {
                    // this is formatted with the invariant culture since it is parsed by VS
                    full = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} {1} DX{2:D4}: {3}",
                        TimeSpanToString(timeDisplay, DateTime.UtcNow - baseTime),

                        // error/warning/info/etc
                        label,

                        // error/warning #
                        eventData.EventId,

                        // error/warning text
                        body);
                }
                else
                {
                    // this is formatted with the invariant culture since it is parsed by VS
                    full = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} DX{1:D4}: {2}",

                        // error/warning/info/etc
                        label,

                        // error/warning #
                        eventData.EventId,

                        // error/warning text
                        body);
                }
            }

            // if args is empty, there is no pip description in the final string
            if (args.Length > 0 && useCustomPipDescription)
            {
                // check whether there is a pip description in the constructed message
                int descriptionStartIndex = full.IndexOf("[Pip");
                if (descriptionStartIndex >= 0)
                {
                    int customDescriptionStartIndex = full.IndexOf(CustomPipDescriptionMarker, descriptionStartIndex);
                    if (customDescriptionStartIndex > descriptionStartIndex)
                    {
                        int commaAfterPipDescIndex = full.IndexOf(',', descriptionStartIndex);
                        // full : "... [PipXXX, <Middle part><Marker><Custom description>..."
                        // remove "<Midle part><Marker>"
                        // full : "... [PipXXX, <Custom description>..."
                        full = full.Remove(commaAfterPipDescIndex + 2, customDescriptionStartIndex + CustomPipDescriptionMarker.Length - commaAfterPipDescIndex - 2);
                    }
                }
            }

            return full;
        }

        /// <inheritdoc />
        protected override void OnCritical(EventWrittenEventArgs eventData)
        {
            Write(eventData, EventLevel.Critical);
        }

        /// <inheritdoc />
        protected override void OnError(EventWrittenEventArgs eventData)
        {
            Write(eventData, EventLevel.Error);
        }

        /// <inheritdoc />
        protected override void OnWarning(EventWrittenEventArgs eventData)
        {
            Write(eventData, EventLevel.Warning);
        }

        /// <inheritdoc />
        protected override void OnInformational(EventWrittenEventArgs eventData)
        {
            Write(eventData, EventLevel.Informational);
        }

        /// <inheritdoc />
        protected override void OnVerbose(EventWrittenEventArgs eventData)
        {
            Write(eventData, EventLevel.Verbose);
        }

        /// <inheritdoc />
        protected override void OnAlways(EventWrittenEventArgs eventData)
        {
            Write(eventData, EventLevel.LogAlways);
        }
    }
}
