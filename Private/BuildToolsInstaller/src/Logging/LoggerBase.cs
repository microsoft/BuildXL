// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildToolsInstaller.Logging
{
    internal abstract class LoggerBase : ILogger
    {
        /// <nodoc />
        public void Warning(string format, params object[] args)
        {
            string warningCommand = string.Format(format, args);
            Warning(warningCommand);
        }

        /// <nodoc />
        public void Error(string format, params object[] args)
        {
            string errorCommand = string.Format(format, args);
            Error(errorCommand);
        }

        /// <nodoc />
        public void Debug(string format, params object[] args)
        {
            string debugCommand = string.Format(format, args);
            Debug(debugCommand);
        }

        /// <nodoc />
        public void Warning(Exception ex, string warningMessage)
        {
            string warningCommand = $"{warningMessage}\n{ex}";
            Warning(warningCommand);
        }

        /// <nodoc />
        public void Error(Exception ex, string errorMessage)
        {
            Error(errorMessage);
            Error($"Full exception details:\n{ex}");
        }

        /// <nodoc />
        public void Error(string message) => Output(ConstructError(message));

        /// <nodoc />
        public void Warning(string message) => Output(ConstructWarning(message));

        /// <nodoc />
        public void Debug(string message) => Output(ConstructDebug(message));

        /// <nodoc />
        public void Info(string message) => Output(ConstructInfo(message));


        // Inheritors should override these with custom formatting and output
        protected abstract string ConstructError(string message);
        protected abstract string ConstructWarning(string message);
        protected abstract string ConstructInfo(string message);
        protected abstract string ConstructDebug(string message);
        protected abstract void Output(string logLine);
    }
}
