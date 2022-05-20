// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using NuGet.Common;

namespace NugetDownloader
{
    /// <summary>
    /// Logs all messages to the console
    /// </summary>
    internal class ConsoleLogger : ILogger
    {
        /// <nodoc/>
        public void Log(LogLevel level, string data)
        {
            Console.WriteLine($"[{level}]:{data}");
        }

        /// <nodoc/>
        public void Log(ILogMessage message)
        {
            Console.WriteLine(message.FormatWithCode());
        }

        /// <nodoc/>
        public Task LogAsync(LogLevel level, string data)
        {
            Log(level, data);
            return Task.CompletedTask;
        }

        /// <nodoc/>
        public Task LogAsync(ILogMessage message)
        {
            Log(message);
            return Task.CompletedTask;

        }

        /// <nodoc/>
        public void LogDebug(string data)
        {
            Log(LogLevel.Debug, data);
        }

        /// <nodoc/>
        public void LogError(string data)
        {
            Log(LogLevel.Error, data);
        }

        /// <nodoc/>
        public void LogInformation(string data)
        {
            Log(LogLevel.Information, data);
        }

        /// <nodoc/>
        public void LogInformationSummary(string data)
        {
            Log(LogLevel.Information, data);
        }

        /// <nodoc/>
        public void LogMinimal(string data)
        {
            Log(LogLevel.Information, data);
        }

        /// <nodoc/>
        public void LogVerbose(string data)
        {
            Log(LogLevel.Verbose, data);
        }

        /// <nodoc/>
        public void LogWarning(string data)
        {
            Log(LogLevel.Warning, data);
        }
    }
}
