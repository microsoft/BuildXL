// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities.Instrumentation.Common;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
#endif

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Captures ETW data pumped through any instance derived from <see cref="Events" /> and redirects output to a
    /// <see cref="IEventWriter" />.
    /// </summary>
    public class TextWriterEventListener : FormattingEventListener
    {
        private readonly IEventWriter m_writer;
        private readonly StoppableTimer m_flushTimer;
        private static readonly TimeSpan s_flushInterval = TimeSpan.FromSeconds(5);
        private readonly PathTranslator m_translator;

        /// <summary>
        /// Creates a new instance configured to output to the given writer.
        /// </summary>
        /// <param name="eventSource">
        /// The event source to listen to.
        /// </param>
        /// <param name="writer">The TextWriter where to send the output. Note that the writer will be disposed when the TextWriterEventListener is itself disposed.</param>
        /// <param name="baseTime">
        /// The UTC time representing time 0 for this listener.
        /// </param>
        /// <param name="warningMapper">
        /// An optional delegate that is used to map warnings into errors or to suppress warnings.
        /// </param>
        /// <param name="level">
        /// The base level of data to be sent to the listener.
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
        /// <param name="pathTranslator">
        /// If specified, translates paths from one root to another
        /// </param>
        /// <param name="listenDiagnosticMessages">
        /// If true, all messages tagged with <see cref="Keywords.Diagnostics" /> at or above <paramref name="level"/> are enabled but not captured unless diagnostics are enabled per-task.
        /// This is useful for StatisticsEventListener, where you need to listen diagnostics messages but capture only ones tagged with CommonInfrastructure task.
        /// </param>
        public TextWriterEventListener(
            Events eventSource,
            IEventWriter writer,
            DateTime baseTime,
            WarningMapper warningMapper = null,
            EventLevel level = EventLevel.Verbose,
            TimeDisplay timeDisplay = TimeDisplay.None,
            EventMask eventMask = null,
            DisabledDueToDiskWriteFailureEventHandler onDisabledDueToDiskWriteFailure = null,
            PathTranslator pathTranslator = null,
            bool listenDiagnosticMessages = false)
            : base(
                eventSource,
                baseTime,
                warningMapper,
                level,
                false,
                timeDisplay,
                eventMask,
                onDisabledDueToDiskWriteFailure,
                listenDiagnosticMessages)
        {
            Contract.Requires(eventSource != null);
            Contract.Requires(writer != null);

            m_writer = writer;

            m_flushTimer = new StoppableTimer(
                SynchronizedFlush,
                (int)s_flushInterval.TotalMilliseconds,
                (int)s_flushInterval.TotalMilliseconds);
            m_translator = pathTranslator;
        }

        /// <summary>
        /// <see cref="TextWriterEventListener(Events, IEventWriter, DateTime, BuildXL.Utilities.Tracing.BaseEventListener.WarningMapper, EventLevel, TimeDisplay, EventMask, BaseEventListener.DisabledDueToDiskWriteFailureEventHandler, PathTranslator, bool)"/>.
        /// </summary>
        public TextWriterEventListener(
            Events eventSource,
            TextWriter writer,
            DateTime baseTime,
            WarningMapper warningMapper = null,
            EventLevel level = EventLevel.Verbose,
            TimeDisplay timeDisplay = TimeDisplay.None,
            EventMask eventMask = null,
            DisabledDueToDiskWriteFailureEventHandler onDisabledDueToDiskWriteFailure = null,
            PathTranslator pathTranslator = null,
            bool listenDiagnosticMessages = false)
            : this(
                eventSource,
                new TextEventWriter(writer),
                baseTime,
                warningMapper,
                level,
                timeDisplay,
                eventMask,
                onDisabledDueToDiskWriteFailure,
                pathTranslator,
                listenDiagnosticMessages)
        {
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public override void Dispose()
        {
            base.Dispose();

            // Make sure the timer and any outstanding callbacks are finished before disposing the writer
            m_flushTimer.Dispose();
            m_writer.Dispose();
        }

        /// <inheritdoc />
        protected override void Output(EventLevel level, int id, string eventName, EventKeywords eventKeywords, string text, bool doNotTranslatePaths = false)
        {
            m_writer.WriteLine(level, (m_translator != null && !doNotTranslatePaths) ? m_translator.Translate(text) : text);

            // At the expense of performance, flush Critical and Error events to the underlying file as they are written
            // so the log can be viewed immediately.
            if (level == EventLevel.Critical ||
                level == EventLevel.Error)
            {
                SynchronizedFlush();
            }
        }

        /// <inheritdoc/>
        protected override void UnsynchronizedFlush()
        {
            m_writer.Flush();
        }

        /// <summary>
        /// Write function for a Text listener only writes messages if the eventData is not OverwritableOnly.
        /// </summary>
        protected override void Write(EventWrittenEventArgs eventData, EventLevel level, string message = null, bool suppressEvent = false)
        {
            if ((eventData.Keywords & Keywords.OverwritableOnly) != EventKeywords.None)
            {
                return;
            }

            base.Write(eventData, level, message, suppressEvent);
        }

        /// <inheritdoc />
        protected override void OnSuppressedWarning(EventWrittenEventArgs eventData)
        {
            Write(eventData, EventLevel.Warning, suppressEvent: true);
        }
    }
}
