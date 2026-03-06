// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using BuildXL.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL
{
    /// <summary>
    /// Captures ETW data pumped through any instance derived from <see cref="Events"/> and
    /// ingests it directly into a Kusto cluster via <see cref="KustoDirectIngestLog"/>.
    /// </summary>
    /// <remarks>
    /// Log lines are formatted in the same PSV schema used by <see cref="BlobWriterEventListener"/>:
    /// <code>
    /// Timestamp|Level|SessionId|ActivityId|RelatedActivityId|EventNumber|Machine|IsWorker|Message
    /// </code>
    /// The <see cref="KustoDirectIngestLog"/> sends batches asynchronously in the background;
    /// writes from this listener are non-blocking and do not affect build latency.
    /// </remarks>
    public sealed class KustoDirectWriterEventListener : FormattingEventListener
    {
        private readonly KustoDirectIngestLog m_kustoLog;
        private readonly PathTranslator? m_translator;
        private readonly bool m_isWorker;
        private readonly LoggingContext m_loggingContext;
        private readonly Action<string> m_errorLogger;

        /// <summary>
        /// Creates a new instance that forwards formatted ETW events to <paramref name="kustoLog"/>.
        /// </summary>
        /// <remarks>
        /// The given <paramref name="kustoLog"/> is owned by this listener and disposed when the listener is disposed.
        /// </remarks>
        public KustoDirectWriterEventListener(
            Events eventSource,
            KustoDirectIngestLog kustoLog,
            DateTime baseTime,
            WarningMapper warningMapper,
            bool isWorker,
            LoggingContext loggingContext,
            Action<string> errorLogger,
            EventLevel level = EventLevel.Verbose,
            TimeDisplay timeDisplay = TimeDisplay.None,
            EventMask? eventMask = null,
            DisabledDueToDiskWriteFailureEventHandler? onDisabledDueToDiskWriteFailure = null,
            PathTranslator? pathTranslator = null,
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
            Contract.Requires(kustoLog != null);
            Contract.Requires(loggingContext != null);
            Contract.Requires(errorLogger != null);

            m_kustoLog = kustoLog;
            m_translator = pathTranslator;
            m_isWorker = isWorker;
            m_loggingContext = loggingContext;
            m_errorLogger = errorLogger;
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            base.Dispose();

            try
            {
                var kustoDisposeStopwatch = new StopwatchVar();

                using (kustoDisposeStopwatch.Start())
                {
                    // Flush the Kusto log synchronously on teardown so any lines buffered since the
                    // last background flush are written before the process exits.
                    m_kustoLog.Dispose();
                }

                Logger.Log.Statistic(
                    m_loggingContext,
                    new Statistic()
                    {
                        Name = Statistics.KustoIngestionDisposeDurationMs,
                        Value = (long)kustoDisposeStopwatch.TotalElapsed.TotalMilliseconds
                    });
            }
            catch (Exception ex)
            {
                m_errorLogger($"[KustoDirectIngest] Error during listener dispose: {ex.GetLogEventMessage()}");
            }
        }

        /// <inheritdoc/>
        protected override void Output(EventLevel level, EventWrittenEventArgs eventData, string text, bool doNotTranslatePaths = false)
        {
            var maybeTranslatedText = (m_translator != null && !doNotTranslatePaths)
                ? m_translator.Translate(text)
                : text;

            // Escape special characters so the PSV structure is preserved.
            maybeTranslatedText = maybeTranslatedText.Replace("|", "!");
            maybeTranslatedText = maybeTranslatedText.Replace("\r\n", " ");
            maybeTranslatedText = maybeTranslatedText.Replace('\n', ' ');

            // PSV schema mirrors the BlobWriterEventListener schema:
            // Timestamp|Level|SessionId|ActivityId|RelatedActivityId|EventNumber|Machine|IsWorker|Message
            var psvLine =
                $"{eventData.TimeStamp.ToUniversalTime():o}" +
                $"|{(int)level}" +
                $"|{m_loggingContext.Session.Id}" +
                $"|{eventData.ActivityId}" +
                $"|{eventData.RelatedActivityId}" +
                $"|{eventData.EventId}" +
                $"|{Environment.MachineName}" +
                $"|{m_isWorker}" +
                $"|{maybeTranslatedText}";

            m_kustoLog.Write(psvLine);
        }

        /// <inheritdoc/>
        protected override void Write(EventWrittenEventArgs eventData, EventLevel level, string? message = null, bool suppressEvent = false)
        {
            // Skip overwrite-only events (e.g. progress line updates) as they are transient.
            if ((eventData.Keywords & Keywords.OverwritableOnly) != EventKeywords.None)
            {
                return;
            }

            base.Write(eventData, level, message, suppressEvent);
        }

        /// <inheritdoc/>
        protected override void OnSuppressedWarning(EventWrittenEventArgs eventData)
        {
            Write(eventData, EventLevel.Warning, suppressEvent: true);
        }
    }
}
