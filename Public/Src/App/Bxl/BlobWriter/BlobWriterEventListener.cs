// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.Logging;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL
{
    /// <summary>
    /// Captures ETW data pumped through any instance derived from <see cref="Events" /> and redirects output to a
    /// <see cref="AzureBlobStorageLog" />.
    /// </summary>
    /// <remarks>
    /// The target blob storage is expected to have an ingestion mechanism setup to insert the logs into a Kusto table. The log
    /// is expected to be sent in PSV (pipe separated value) format to match the schema
    /// .create-merge table ADOMessages (Timestamp:datetime, Level:int, SessionId:guid, ActivityId:guid, RelatedActivityId:guid, EventNumber:int, Machine:string, IsWorker:bool, Message:string)  
    /// </remarks>
    public class BlobWriterEventListener : FormattingEventListener
    {
        private readonly AzureBlobStorageLog m_blobLog;
        private readonly PathTranslator m_translator;
        private readonly bool m_isWorker;
        private readonly LoggingContext m_loggingContext;
        private readonly Action<string> m_errorLogger;

        /// <summary>
        /// Creates a new instance configured to output to the given writer.
        /// </summary>
        /// <param name="eventSource">
        /// The event source to listen to.
        /// </param>
        /// <param name="blobLog">The blob storage instance where logs are to be uploaded to. The instance is expected to not be started.</param>
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
        /// <param name="isWorker">Whether the machine is a worker in a distributed build</param>
        /// <param name="loggingContext">The logging context to use for logging</param>
        /// <param name="errorLogger">A provided function used to log errors</param>
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
        /// <exception cref="Exception">If the provided <paramref name="blobLog"/> throws at startup</exception>
        public BlobWriterEventListener(
            Events eventSource,
            AzureBlobStorageLog blobLog,
            DateTime baseTime,
            WarningMapper warningMapper,
            bool isWorker,
            LoggingContext loggingContext,
            Action<string> errorLogger,
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
            Contract.Requires(blobLog != null);
            Contract.Requires(!blobLog.StartupStarted);

            m_blobLog = blobLog;
            m_translator = pathTranslator;
            m_isWorker = isWorker;
            m_loggingContext = loggingContext;
            m_errorLogger = errorLogger;

            // Any auth errors will be surfaced on startup. Make sure we throw in this case.
            var startupResult = m_blobLog.StartupAsync().GetAwaiter().GetResult();
            startupResult.RethrowIfFailure();
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public override void Dispose()
        {
            base.Dispose();
            var result = m_blobLog.ShutdownAsync().GetAwaiter().GetResult();

            // It is unlikely we hit an error on shutdown (most errors should be caught on startup). But since some uploads
            // may happen on dispose, surface this to the user
            if (!result.Succeeded)
            {
                m_errorLogger(result.ErrorMessage);
            }
        }

        /// <inheritdoc />
        protected override void Output(EventLevel level, EventWrittenEventArgs eventData, string text, bool doNotTranslatePaths = false)
        {
            var maybeTranslatedText = (m_translator != null && !doNotTranslatePaths) ? m_translator.Translate(text) : text;

            // we are going to use pipe separated values, so we need to escape any pipes in the text
            maybeTranslatedText = maybeTranslatedText.Replace("|", "!");
            // Replace all potential new lines with a space.
            // Be platform-agnostic, as tools might also behave like that (e.g., using CRLF on Linux)
            maybeTranslatedText = maybeTranslatedText.Replace("\r\n", " ");
            maybeTranslatedText = maybeTranslatedText.Replace('\n', ' ');
            
            // The ingestion pipeline expectes a PSV (pipe separated value) file with the following format:
            //
            // .create-or-alter table ADOMessages ingestion csv mapping "Ingestion"
            //'['
            //'  {"Column": "Timestamp", "Properties": {"Ordinal": "0"}},'
            //'  {"Column": "Level", "Properties": {"Ordinal": "1"}},'
            //'  {"Column": "SessionId", "Properties": {"Ordinal": "2"}},'
            //'  {"Column": "ActivityId", "Properties": {"Ordinal": "3"}},'
            //'  {"Column": "RelatedActivityId", "Properties": {"Ordinal": "4"}},'
            //'  {"Column": "EventNumber", "Properties": {"Ordinal": "5"}},'
            //'  {"Column": "Machine", "Properties": {"Ordinal": "6"}},'
            //'  {"Column": "IsWorker", "Properties": {"Ordinal": "7"}},'
            //'  {"Column": "Message", "Properties": {"Ordinal": "8"}},'
            //']'
            var psvLine = $"{eventData.TimeStamp.ToUniversalTime():o}|{(int)eventData.Level}|{m_loggingContext.Session.Id}|{eventData.ActivityId}|{eventData.RelatedActivityId}|{eventData.EventId}|{Environment.MachineName}|{m_isWorker}|{maybeTranslatedText}";
            m_blobLog.Write(psvLine);
        }

        /// <summary>
        /// Write function for a blob listener only writes messages if the eventData is not OverwritableOnly.
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