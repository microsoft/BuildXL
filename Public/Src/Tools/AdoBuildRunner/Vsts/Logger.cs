// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Newtonsoft.Json;

namespace BuildXL.AdoBuildRunner.Vsts
{
    /// <summary>
    /// This is a special logger for VSTS task using the special vso logging commands ##vso
    /// </summary>
    public class Logger : ILogger
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
        public void Error(string message)
        {
            string errorCommand = string.Format(LogErrorFormat, WithTimeStamp(message));
            Console.WriteLine(errorCommand);
        }

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
            string warningCommand = $"{warningMessage}\n{JsonConvert.SerializeObject(ex)}";
            Warning(warningCommand);
        }

        /// <nodoc />
        public void Error(Exception ex, string errorMessage)
        {
            Error(errorMessage);
            Error($"Full exception details:\n{JsonConvert.SerializeObject(ex)}");
        }

        /// <nodoc />
        public void Warning(string message)
        {
            string warningCommand = string.Format(LogWarningFormat, WithTimeStamp(message));
            Console.WriteLine(warningCommand);
        }

        /// <nodoc />
        public void Debug(string message)
        {
            string debugCommand = string.Format(LogDebugFormat, WithTimeStamp(message));
            Console.WriteLine(debugCommand);
        }

        /// <nodoc />
        public void Info(string message)
        {
            Console.WriteLine(WithTimeStamp(message));
        }

        private string WithTimeStamp(string message) => string.Format("[{0}] {1}", DateTime.UtcNow.ToString("HH:mm:ss.ff"), message);
    }
}
