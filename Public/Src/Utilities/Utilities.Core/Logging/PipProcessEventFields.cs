// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;

#nullable disable

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// struct contain all fields of the original PipProcessError/PipProcessWarning event
    /// </summary>
    /// <remarks>
    /// Since both the events mostly have common fields a single struct is created for both the events.
    /// </remarks>
    public struct PipProcessEventFields
    {
        /// <nodoc />
        public long PipSemiStableHash { get; }

        /// <nodoc />
        public string PipDescription { get; }

        /// <nodoc />
        public string PipSpecPath { get; }

        /// <nodoc />
        public string PipWorkingDirectory { get; }

        /// <nodoc />
        public string PipExe { get; }

        /// <nodoc />
        public string OutputToLog { get; }

        /// <nodoc />
        public string MessageAboutPathsToLog { get; }

        /// <nodoc />
        public string PathsToLog { get; }

        /// <nodoc />
        public int ExitCode { get; }

        /// <nodoc />
        public string OptionalMessage { get; }

        /// <nodoc />
        public string ShortPipDescription { get; }

        /// <nodoc />
        public long PipExecutionTimeMs { get; }

        /// <summary>
        /// Construct PipProcessEventFields from eventPayload
        /// </summary>
        public PipProcessEventFields(ReadOnlyCollection<object> eventPayload, bool forwardedPayload, bool isPipProcessError)
        {
            // When the PipProcessEvent is forwarded from worker it is encapsulated in a WorkerForwardedEvent, which has 4 other fields in front of the real pipProcessEvent.
            // So the actual event starts at index 4.
            // PipProcessWarningEvent does not contain the following fields - ExitCode, PipExecutionTimeMs, ShortPipDescription, OptionalMessage. These fields need to be empty when creating PipProcessWarningEvent.

            var startIndex = forwardedPayload ? 4 : 0;
#pragma warning disable CS8600
#pragma warning disable CS8601
            PipDescription = (string)eventPayload[1 + startIndex];
            PipSpecPath = (string)eventPayload[2 + startIndex];
            PipWorkingDirectory = (string)eventPayload[3 + startIndex];
            PipExe = (string)eventPayload[4 + startIndex];
            OutputToLog = (string)eventPayload[5 + startIndex];
            MessageAboutPathsToLog = (string)eventPayload[6 + startIndex];
            PathsToLog = (string)eventPayload[7 + startIndex];
            if (isPipProcessError)
            {
                OptionalMessage = (string)eventPayload[9 + startIndex];
                ShortPipDescription = (string)eventPayload[10 + startIndex];
                PipExecutionTimeMs = (long)eventPayload[11 + startIndex];
            }
#pragma warning restore CS8600
#pragma warning restore CS8601

#pragma warning disable CS8605
            PipSemiStableHash = (long)eventPayload[0 + startIndex];
            if (isPipProcessError)
            {
                ExitCode = (int)eventPayload[8 + startIndex];
            }
#pragma warning restore CS8605
        }

        /// <summary>
        /// Construct PipProcessErrorEventFields
        /// This constructor is to be used for PipProcessError event since this event has a couple of fields extra.
        /// </summary>
        public PipProcessEventFields(
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string pipExe,
            string outputToLog,
            string messageAboutPathsToLog,
            string pathsToLog,
            int exitCode,
            string optionalMessage,
            string shortPipDescription,
            long pipExecutionTimeMs
            )
        {
            PipSemiStableHash = pipSemiStableHash;
            PipDescription = pipDescription;
            PipSpecPath = pipSpecPath;
            PipWorkingDirectory = pipWorkingDirectory;
            PipExe = pipExe;
            OutputToLog = outputToLog;
            MessageAboutPathsToLog = messageAboutPathsToLog;
            PathsToLog = pathsToLog;
            ExitCode = exitCode;
            OptionalMessage = optionalMessage;
            ShortPipDescription = shortPipDescription;
            PipExecutionTimeMs = pipExecutionTimeMs;
        }

        /// <summary>
        /// Constructor for PipProcessWarning
        /// </summary>
        public PipProcessEventFields(
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string pipExe,
            string outputToLog,
            string messageAboutPathsToLog,
            string pathsToLog
            )
        {
            PipSemiStableHash = pipSemiStableHash;
            PipDescription = pipDescription;
            PipSpecPath = pipSpecPath;
            PipWorkingDirectory = pipWorkingDirectory;
            PipExe = pipExe;
            OutputToLog = outputToLog;
            MessageAboutPathsToLog = messageAboutPathsToLog;
            PathsToLog = pathsToLog;
        }

        /// <summary>
        /// Creates PipProcessEvent object for PipProcessWarnings.
        /// </summary>
        public static PipProcessEventFields CreatePipProcessWarningEventFields(
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string pipExe,
            string outputToLog,
            string messageAboutPathsToLog,
            string pathsToLog)
        {
            return new PipProcessEventFields(
                pipSemiStableHash,
                pipDescription,
                pipSpecPath,
                pipWorkingDirectory,
                pipExe,
                outputToLog,
                messageAboutPathsToLog,
                pathsToLog);            
        }

        /// <summary>
        /// Creates PipProcessEvent object for PipProcessErrors.
        /// </summary>
        public static PipProcessEventFields CreatePipProcessErrorEventFields(
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string pipExe,
            string outputToLog,
            string messageAboutPathsToLog,
            string pathsToLog,
            int exitCode,
            string optionalMessage,
            string shortPipDescription,
            long pipExecutionTimeMs)
        {
            return new PipProcessEventFields(
                pipSemiStableHash,
                pipDescription,
                pipSpecPath,
                pipWorkingDirectory,
                pipExe,
                outputToLog,
                messageAboutPathsToLog,
                pathsToLog,
                exitCode,
                optionalMessage,
                shortPipDescription,
                pipExecutionTimeMs);
        }
    }
}