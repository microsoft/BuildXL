// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// Implementation of <see cref="ILogger"/> that forwards log requests to System.Console.
    /// </summary>
    public sealed class ConsoleLogger : ILogger
    {
        /// <nodoc />
        public bool IsLoggingVerbose { get; }

        /// <nodoc />
        public string Prefix { get; }

        /// <param name="logVerbose">Whether it should log verbose messages.</param>
        /// <param name="prefix">Optional prefix to prepend to every log message.</param>
        public ConsoleLogger(bool logVerbose, string prefix = null)
        {
            IsLoggingVerbose = logVerbose;
            Prefix = prefix ?? string.Empty;
        }

        /// <summary>
        /// Logs to Console.Out, unless <paramref name="level"/> is <see cref="Interfaces.LogLevel.Verbose"/>,
        /// and <see cref="IsLoggingVerbose"/> is <code>false</code>.
        /// </summary>
        public void Log(LogLevel level, string format, params object[] args)
        {
            if (!IsLoggingVerbose && level == LogLevel.Verbose)
            {
                return;
            }

            var message = Prefix + LoggerExtensions.Format(level, format, args);
            var writer = level == LogLevel.Error ? Console.Error : Console.Out;
            writer.WriteLine(message);
        }

        /// <nodoc />
        public void Dispose() { }
    }
}
