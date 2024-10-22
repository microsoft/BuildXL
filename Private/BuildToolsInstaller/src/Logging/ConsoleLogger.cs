// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildToolsInstaller.Logging
{
    /// <summary>
    /// This is a special logger for VSTS task using the special vso logging commands ##vso
    /// </summary>
    internal class ConsoleLogger : LoggerBase
    {
        protected override string ConstructError(string message) => $"ERROR: {message}";
        protected override string ConstructWarning(string message) => $"WARN: {message}";
        protected override string ConstructInfo(string message) => $"INFO: {message}";
        protected override string ConstructDebug(string message) => $"DEBUG: {message}";
        protected override void Output(string logLine) => Console.WriteLine(string.Format("[{0}] {1}", DateTime.UtcNow.ToString("HH:mm:ss.ff"), logLine));
    }
}
