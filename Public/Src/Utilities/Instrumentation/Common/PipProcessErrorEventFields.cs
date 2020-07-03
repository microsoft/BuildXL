// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// struct contain all fields of the original PipProcessError event
    /// </summary>
    public struct PipProcessErrorEventFields
    {
        /// <summary>
        /// No doc
        /// </summary>
        public long PipSemiStableHash { get; }

        /// <summary>
        /// No doc
        /// </summary>
        public string PipDescription { get; }

        /// <summary>
        /// No doc
        /// </summary>
        public string PipSpecPath { get; }

        /// <summary>
        /// No doc
        /// </summary>
        public string PipWorkingDirectory { get; }

        /// <summary>
        /// No doc
        /// </summary>
        public string PipExe { get; }

        /// <summary>
        /// No doc
        /// </summary>
        public string OutputToLog { get; }

        /// <summary>
        /// No doc
        /// </summary>
        public string MessageAboutPathsToLog { get; }

        /// <summary>
        /// No doc
        /// </summary>
        public string PathsToLog { get; }

        /// <summary>
        /// No doc
        /// </summary>
        public int ExitCode { get; }

        /// <summary>
        /// No doc
        /// </summary>
        public string OptionalMessage { get; }

        /// <summary>
        /// No doc
        /// </summary>
        public string ShortPipDescription { get; }

        /// <summary>
        /// Construct PipProcessErrorEventFields from eventPayload
        /// </summary>
        public PipProcessErrorEventFields(ReadOnlyCollection<object?> eventPayload, bool forwardedPayload)
        {
            // When the PipProcessErrorEvent is forwarded from worker it is ecapsulated in a WorkerForwardedEvent, which has 4 other fields in front of the real pipProcessEvent.
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
            string shortPipDescription
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
        }
    }
}