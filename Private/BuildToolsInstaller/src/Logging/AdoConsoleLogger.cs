// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildToolsInstaller.Logging
{
    /// <summary>
    /// This is a special logger for VSTS task using the special vso logging commands ##vso
    /// </summary>
    internal class AdoConsoleLogger : LoggerBase
    {
        /// <summary>
        /// This is the vso format for writing an error to the agent console where {0} is the message
        /// </summary>
        private const string LogErrorFormat = "##vso[task.logissue type=error;]{0}";

        /// <summary>
        /// This is the vso format for writing a warning to the agent console where {0} is the message
        /// </summary>
        private const string LogWarningFormat = "##vso[task.logissue type=warning;]{0}";

        /// <summary>
        /// This is the vso format for writing debug info to the agent console where {0} is the message
        /// </summary>
        private const string LogDebugFormat = "##vso[task.debug]{0}";

        /// <nodoc />
        protected override string ConstructError(string message) => string.Format(LogErrorFormat, WithTimeStamp(message));

        /// <nodoc />
        protected override string ConstructWarning(string message) => string.Format(LogWarningFormat, WithTimeStamp(message));

        /// <nodoc />
        protected override string ConstructDebug(string message) =>  string.Format(LogDebugFormat, WithTimeStamp(message));

        /// <nodoc />
        protected override string ConstructInfo(string message) => WithTimeStamp(message);

        private string WithTimeStamp(string message) => string.Format("[{0}] {1}", DateTime.UtcNow.ToString("HH:mm:ss.ff"), message);

        /// <nodoc />
        protected override void Output(string logLine) => Console.WriteLine(logLine);
    }
}
