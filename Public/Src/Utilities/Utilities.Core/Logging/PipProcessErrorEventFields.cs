// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;

#nullable disable

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// struct contain all fields of the original PipProcessError event
    /// </summary>
    public struct PipProcessErrorEventFields
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
        /// Construct PipProcessErrorEventFields from eventPayload
        /// </summary>
        public PipProcessErrorEventFields(ReadOnlyCollection<object> eventPayload, bool forwardedPayload)
        {
            // When the PipProcessErrorEvent is forwarded from worker it is encapsulated in a WorkerForwardedEvent, which has 4 other fields in front of the real pipProcessEvent.
            // So the actual event starts at index 4.

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

            OptionalMessage = (string)eventPayload[9 + startIndex];
            ShortPipDescription = (string)eventPayload[10 + startIndex];
            PipExecutionTimeMs = (long)eventPayload[11 + startIndex];
#pragma warning restore CS8600
#pragma warning restore CS8601

#pragma warning disable CS8605
            PipSemiStableHash = (long)eventPayload[0 + startIndex];
            ExitCode = (int)eventPayload[8 + startIndex];
#pragma warning restore CS8605
        }

        /// <summary>
        /// Construct PipProcessErrorEventFields
        /// </summary>
        public PipProcessErrorEventFields(
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
    }
}