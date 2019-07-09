// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.IO;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Captures ETW warning and error data pumped through any instance derived from <see cref="Events" /> and redirects output to a
    /// <see cref="TextWriter" />.
    /// </summary>
    public sealed class ErrorAndWarningEventListener : TextWriterEventListener
    {
        private readonly bool m_logErrors;
        private readonly bool m_logWarnings;

        /// <summary>
        /// Creates a new instance configured to output to the given writer.
        /// </summary>
        /// <param name="eventSource">
        /// The event source to listen to.
        /// </param>
        /// <param name="writer">The TextWriter where to send the output.</param>
        /// <param name="baseTime">The UTC time when the build started.</param>
        /// <param name="warningMapper">
        /// An optional delegate that is used to map warnings into errors or to suppress warnings.
        /// </param>
        /// <param name="logErrors">
        /// When true, will log errors.
        /// </param>
        /// <param name="logWarnings">
        /// When true, will log warnings.
        /// </param>
        /// <param name="pathTranslator">
        /// If specified, translates paths from one root to another
        /// </param>
        /// <param name="timeDisplay">
        /// The kind of time prefix to apply to each chunk of output.
        /// </param>
        public ErrorAndWarningEventListener(
            Events eventSource,
            TextWriter writer,
            DateTime baseTime,
            bool logErrors,
            bool logWarnings,
            WarningMapper warningMapper = null,
            PathTranslator pathTranslator = null,
            TimeDisplay timeDisplay = TimeDisplay.None)
            : base(
                eventSource,
                writer,
                baseTime,
                warningMapper,
                logWarnings ? EventLevel.Warning :

                    // The error listener must listen to both warnings and errors, since warnings can be promoted
                    // to errors. When that happens, they need to trigger the error event listener to ensure
                    // they get logged in the error log file
                    (EventLevel.Error | EventLevel.Warning),
                pathTranslator: pathTranslator,
                timeDisplay: timeDisplay)
        {
            Contract.Requires(eventSource != null);
            Contract.Requires(writer != null);

            m_logErrors = logErrors;
            m_logWarnings = logWarnings;
        }

        /// <inheritdoc />
        protected override void OnCritical(EventWrittenEventArgs eventData)
        {
            if (m_logErrors)
            {
                base.OnCritical(eventData);
            }
        }

        /// <inheritdoc />
        protected override void OnWarning(EventWrittenEventArgs eventData)
        {
            if (m_logWarnings)
            {
                base.OnWarning(eventData);
            }
        }

        /// <inheritdoc />
        protected override void OnError(EventWrittenEventArgs eventData)
        {
            if (m_logErrors)
            {
                base.OnError(eventData);
            }
        }

        /// <inheritdoc />
        protected override void OnInformational(EventWrittenEventArgs eventData)
        {
        }

        /// <inheritdoc />
        protected override void OnVerbose(EventWrittenEventArgs eventData)
        {
        }

        /// <inheritdoc />
        protected override void OnAlways(EventWrittenEventArgs eventData)
        {
        }

        /// <inheritdoc />
        protected override void OnSuppressedWarning(EventWrittenEventArgs eventData)
        {   
        }
    }
}
